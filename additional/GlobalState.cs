using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ASFS.UI.Windows.Games;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Newtonsoft.Json;

namespace ASFS
{
    public class GlobalState
    {
        private static TcpClient client;
        public static NetworkStream stream;
        public static AppSettings settings { get; set; } = LoadSettings();

        public static MainWindow MainWindow { get; set; }

        public static string UserEmail { get; set; }
        public static string UserStatus { get; set; }
        public static string UserExpireDate { get; set; }

        public static string CurrentLogin { get; set; } = "";

        public static float NeedToPay
        {
            get
            {
                return UserStatus switch
                {
                    "free" => 150.0f,
                    "standard" => 200.0f,
                    "basic" => 300.0f,
                    "plus" => 350.0f,
                    "premium" => 450.0f,
                    _ => 150.0f
                };
            }
        }

        public enum CurrentBotType : ushort
        {
            None = 0,
            Dolphin = 1,
            Chrome = 2,
            OnlyFansPosting = 3,
        }

        public static CurrentBotType CurrentBot = CurrentBotType.None;
        
        private static string _selectedProfileName = "None";
        private static string dolphinSelectedProfileName = "None";
        private static string chromeSelectedProfileName = "None";
        public static Profile dolphinSelectedProfile { get; set; }
        public static Profile chromeSelectedProfile { get; set; }
        
        public static OFProfile ofSelectedProfile { get; set; }
        public static List<OFProfile> ofSelectedProfiles { get; set; } = new List<OFProfile>();

        public static ObservableCollection<Profile> profiles { get; set; } = new ObservableCollection<Profile>();
        public static ObservableCollection<OFProfile> ofProfiles { get; set; } = new ObservableCollection<OFProfile>();
        public static ObservableCollection<SFSBot> dolphinActiveBots { get; set; } = new ObservableCollection<SFSBot>();
        public static ObservableCollection<SFSBot> chromeActiveBots { get; set; } = new ObservableCollection<SFSBot>();

        public static ObservableCollection<Profile> dolphinQueue { get; set; } = new ObservableCollection<Profile>();
        public static ObservableCollection<Profile> chromeQueue { get; set; } = new ObservableCollection<Profile>();


        public static ObservableCollection<SortingGroup>? sortingGroups { get; set; } =
            new ObservableCollection<SortingGroup>();

        public static ClientHelper clientHelper = new ClientHelper();

        public static bool isConnected = false;
        public static bool isOnlineModeEnable = true;
        public static bool BetaAccess = false;
        public static string CurrentApplicationBuildVersion = "2.0.1.1 public build";

        public static bool isWaitingForReponse = false;

        public static readonly ConcurrentQueue<string> ResponseQueue = new ConcurrentQueue<string>();


        public static int ConnectionToServer()
        {
            try
            {
                if (!isConnected)
                {
                    /**
                     * Подключаемся к серверу по порту и IP адресу, создавая прямое TCP соединение
                     */
                    client = new TcpClient();
                    client.Connect("95.164.68.160", 6664);
                    stream = client.GetStream();
                    isConnected = true;

                    Task.Run(() => ListenToServer());
                    return 0;
                }

                return 0;
            }
            catch (Exception ex)
            {
                clientHelper.Log("Error connecting to server: " + ex.Message);
                return 1;
            }
        }

        public static void EnqueueResponse(string response)
        {
            ResponseQueue.Enqueue(response);
        }
        public static async Task EnqueueResponseAsync(string response)
        {
            ResponseQueue.Enqueue(response);
        }
        
        public static void ClearResponseQueue()
        {
            while (ResponseQueue.TryDequeue(out _)) { }
        }

        /**
         * Функция для прослушивания запросов с сервера
         */
        private static async Task ListenToServer()
        {
            byte[] buffer = new byte[131072];
            StringBuilder messageBuilder = new StringBuilder();

            try
            {
                while (isConnected)
                {
                    if(isWaitingForReponse) continue;
                    int byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (byteCount == 0)
                    {
                        isConnected = false;
                        break;
                    }

                    string messagePart = Encoding.UTF8.GetString(buffer, 0, byteCount);
                    messageBuilder.Append(messagePart);

                    while (messageBuilder.ToString().Contains("\n"))
                    {
                        string completeMessage = messageBuilder.ToString();
                        int newlineIndex = completeMessage.IndexOf('\n');
                        string message = completeMessage.Substring(0, newlineIndex).Trim();
                        messageBuilder.Remove(0, newlineIndex + 1);

                        if (string.IsNullOrEmpty(message)) 
                        {
                            continue;
                        }

                        if (message.Equals("GET_STATUS"))
                        {
                            if (!ResponseQueue.TryDequeue(out string response))
                            {
                                await SendMessageAsync("OK");
                            }
                            else
                            {
                                await SendMessageAsync(response);
                            }
                        }
                        else if (completeMessage != null && completeMessage.StartsWith("CLOSE_APP"))
                        {
                            Console.WriteLine("Shutting down...");
                            clientHelper.CloseApp(
                                Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime);
                        }
                        else if (completeMessage != null && completeMessage.StartsWith("START_GAME"))
                        {
                            Console.WriteLine("Starting game...");
                            await Dispatcher.UIThread.InvokeAsync(() => { MainWindow.StartGame(); });
                        }
                        else
                        {
                            Console.WriteLine("Unknown message: " + message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                clientHelper.Log("Error: " + ex.Message);
                isConnected = false;
            }
            finally
            {
                isWaitingForReponse = false;
            }
        }

        /**
         * Функция возвращает строку ответа с сервера,
         *  вызывая её вы слушаете ответ сервера на любой запрос
         */
        public static async Task<string> WaitForServerResponse(string responseTag)
        {
            isWaitingForReponse = true;
            StringBuilder messageBuilder = new StringBuilder();
            clientHelper.Log("Start waiting for response...");

            try
            {
                while (true)
                {
                    string messagePart = await ReadNextMessageAsync();
                    messageBuilder.Append(messagePart);

                    string completeMessage = ReadNextMessage(ref messageBuilder);
                    if (completeMessage != null && !completeMessage.Equals("CLOSE_APP") && !completeMessage.Equals("GET_STATUS") && completeMessage.StartsWith(responseTag))
                    {
                        string clearedResponse = completeMessage.Replace(responseTag, "").Trim();
                        Console.WriteLine("Response: " + clearedResponse);
                        return completeMessage;
                    }
                }
            }
            catch (Exception ex)
            {
                clientHelper.Log("Error: " + ex.Message);
                isConnected = false;
            }
            finally
            {
                isWaitingForReponse = false;
            }

            return null;
        }

        private static async Task<string> ReadNextMessageAsync()
        {
            byte[] buffer = new byte[131072];
            int byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);

            if (byteCount == 0)
            {
                isConnected = false;
                return string.Empty;
            }

            return Encoding.UTF8.GetString(buffer, 0, byteCount);
        }

        private static string ReadNextMessage(ref StringBuilder messageBuilder)
        {
            string fullMessage = null;
            string messageStr = messageBuilder.ToString();
            int newlineIndex = messageStr.IndexOf('\n');

            if (newlineIndex >= 0)
            {
                fullMessage = messageStr.Substring(0, newlineIndex).Trim();
                messageBuilder.Remove(0, newlineIndex + 1);
            }

            return fullMessage;
        }

        public static async Task SendMessageAsync(string message)
        {
            /**
             * Функция для проброса на сервер своего сообщения,
             *  вызывается при слушаньи серверных запросов
             */
            byte[] messageBytes = Encoding.UTF8.GetBytes(message + "\n");
            await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
            await stream.FlushAsync();
        }

        /**
         * Функция соединяет сортировачные группы и профиля пользователя
         *
         * Если профили или сортировачные группы равны нулю, функция возвращается
         */
        public static void AssignSortingGroupDetailsToProfiles()
        {
            if (profiles == null || sortingGroups == null)
            {
                return;
            }

            /**
             * Каждый профиль перебирается из списка десериализованных профилей
             * Каждому профилю присваивается сортировочная группа в соответствии с данными из профиля
             * После чего профиль также получает кастомный цвет сортировки
             */
            foreach (Profile profile in profiles)
            {
                SortingGroup sortingGroup = sortingGroups.FirstOrDefault(sg => sg.GroupID == profile.SortingGroupID);
                if (sortingGroup != null)
                {
                    profile.SortingGroupName = sortingGroup.GroupName;
                    profile.SortingGroupColor = MyColorConverter.ConvertHexToBrush(sortingGroup.GroupColor);
                }
                else
                {
                    profile.SortingGroupName = null;
                    profile.SortingGroupColor = null;
                }
            }
        }


        public static AppSettings LoadSettings()
        {
            var settings = AppSettings.Load("appsettings.json");
            if (settings == null)
            {
                throw new InvalidOperationException("Failed to load settings from appsettings.json");
            }

            return settings;
        }

        public static void SetSelectedProfile(Profile profile)
        {
            switch (CurrentBot)
            {
                case CurrentBotType.Dolphin:
                    dolphinSelectedProfile = profile;
                    break;
                case CurrentBotType.Chrome:
                    chromeSelectedProfile = profile;
                    break;
                default:
                    dolphinSelectedProfile = profile;
                    break;
            }
        }

        public static void RemoveActiveBot(SFSBot bot)
        {
            if (bot is DolphinBot)
                dolphinActiveBots.Remove(bot);
            else if (bot is ChromeBot)
            {
                chromeActiveBots.Remove(bot);
            }
        }

        public static void AddActiveBot(SFSBot bot)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                if (bot is DolphinBot)
                    dolphinActiveBots.Add(bot);
                else if (bot is ChromeBot)
                {
                    chromeActiveBots.Add(bot);
                }
            });
        }

        public static ObservableCollection<SFSBot> GetActiveBots()
        {
            return Dispatcher.UIThread.Invoke(() =>
            {
                switch (CurrentBot)
                {
                    case CurrentBotType.Dolphin:
                        return dolphinActiveBots;
                    case CurrentBotType.Chrome:
                        return chromeActiveBots;
                    default:
                        return dolphinActiveBots;
                }
            });
        }

        public static void AddProfilesToQueue(ObservableCollection<Profile> profiles)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                switch (CurrentBot)
                {
                    case CurrentBotType.Dolphin:
                        foreach (Profile profile in profiles)
                        {
                            Console.WriteLine(profile.ProfileName + " was added to queue");
                            dolphinQueue.Add(profile);
                        }

                        break;
                    case CurrentBotType.Chrome:
                        foreach (Profile profile in profiles)
                        {
                            chromeQueue.Add(profile);
                        }

                        break;
                    default:
                        foreach (Profile profile in profiles)
                        {
                            dolphinQueue.Add(profile);
                        }

                        break;
                }
            });
        }

        public static async Task LoadProfilesFromServer()
        {
            await GlobalState.EnqueueResponseAsync("GetProfiles");
            Console.WriteLine("Try to get profiles");
            string serverResponse = await GlobalState.WaitForServerResponse("GetProfiles");

            if (serverResponse.Contains("Successful"))
            {
                string profilesJson = serverResponse.Replace("GetProfilesSuccessful ", "");
                clientHelper.Log("Profiles JSON: " + profilesJson);

                if (!string.IsNullOrEmpty(profilesJson))
                {
                    GlobalState.profiles = JsonConvert.DeserializeObject<ObservableCollection<Profile>>(profilesJson);

                    foreach (Profile profile in GlobalState.profiles)
                    {
                        Console.WriteLine(
                            $@"UUID: {profile.ProfileUUID} Profile Name: {profile.ProfileName} Proxy: {profile.ProfileProxy}");
                    }
                }
            }
            else
            {
                clientHelper.Log("No profiles received");
            }
        }

        public static async Task LoadOFProfilesFromServer()
        {
            await GlobalState.EnqueueResponseAsync("GetOFProfiles");
            Console.WriteLine("Try to get profiles");
            string serverResponse = await GlobalState.WaitForServerResponse("GetOFProfiles");

            if (serverResponse.Contains("Successful"))
            {
                string profilesJson = serverResponse.Replace("GetOFProfilesSuccessful ", "");
                clientHelper.Log("Profiles JSON: " + profilesJson);

                if (!string.IsNullOrEmpty(profilesJson))
                {
                    GlobalState.ofProfiles = JsonConvert.DeserializeObject<ObservableCollection<OFProfile>>(profilesJson);

                    foreach (OFProfile profile in GlobalState.ofProfiles)
                    {
                        Console.WriteLine(
                            $@"UUID: {profile.UUID} Profile Name: {profile.ProfileName} AdsID: {profile.AdsID}");
                    }
                }
            }
            else
            {
                clientHelper.Log("No profiles received");
            }
        }

        public static async Task LoadSortingGroupsFromServer()
        {
            GlobalState.EnqueueResponse("GetSortingGroups");
            Console.WriteLine("Try to get groups");
            string serverResponse = await GlobalState.WaitForServerResponse("GetSortingGroups");

            if (serverResponse.StartsWith("GetSortingGroupsSuccessful"))
            {
                string sortingGroupsJson = serverResponse.Replace("GetSortingGroupsSuccessful ", "");
                clientHelper.Log("Sorting groups JSON: " + sortingGroupsJson);

                if (!string.IsNullOrEmpty(sortingGroupsJson))
                {
                    GlobalState.sortingGroups =
                        JsonConvert.DeserializeObject<ObservableCollection<SortingGroup>>(sortingGroupsJson);

                    GlobalState.AssignSortingGroupDetailsToProfiles();

                    foreach (SortingGroup sortingGroup in GlobalState.sortingGroups)
                    {
                        Console.WriteLine(
                            $"GroupName: {sortingGroup.GroupName} GroupID: {sortingGroup.GroupID}");
                    }
                }
            }
        }

        public static async Task GetEmailAndStatus(string userLogin)
        {
            GlobalState.CurrentLogin = userLogin;
            GlobalState.clientHelper.Log("Try to get e-mail and status");
            string requestTag = "GetUserEmailAndStatus";

            string request = $"GetUserEmailAndStatus\n{userLogin}\n";

            await GlobalState.EnqueueResponseAsync(request);

            string serverResponse = await GlobalState.WaitForServerResponse(requestTag);

            if (serverResponse.Contains("Successful"))
            {
                GlobalState.UserEmail = serverResponse.Split('|')[1];
                GlobalState.UserStatus = serverResponse.Split('|')[2];
                GlobalState.UserExpireDate = serverResponse.Split('|')[3];
                GlobalState.CurrentLogin = serverResponse.Split('|')[4];
            }

            GlobalState.clientHelper.Log("E-mail: " + GlobalState.UserEmail);
            GlobalState.clientHelper.Log("Status: " + GlobalState.UserStatus);
            GlobalState.clientHelper.Log("Expire date: " + GlobalState.UserExpireDate);
            GlobalState.clientHelper.Log("Login: " + GlobalState.CurrentLogin);
        }



        public static class MyColorConverter
        {
            /**
             * Класс и метод конвертации цвета из HEX строки в Brush формат
             */
            public static IBrush ConvertHexToBrush(string hexColor)
            {
                Console.WriteLine("try to get color");
                return new SolidColorBrush(Color.Parse(hexColor));
            }
        }

        public static void SetSelectedOFProfile(OFProfile selectedOfProfile)
        {
            ofSelectedProfile = selectedOfProfile;
        }

        public static void SetSelectedOFProfiles(List<OFProfile> selectedProfiles)
        {
            ofSelectedProfiles = selectedProfiles;
        }
    }
}
