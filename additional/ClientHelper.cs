using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using NAudio.Wave;

namespace ASFS
{
    public class ClientHelper
    {
        public bool IsEmailCorrect(string inputMail)
        {
            string pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
    
            return Regex.IsMatch(inputMail, pattern);
        }

        public void ShowErrorMessage(TextBlock messageTextBlock, string message)
        {
            messageTextBlock.Text = message;
            messageTextBlock.IsVisible = true;

            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(15);
            timer.Tick += (sender, e) =>
            {
                messageTextBlock.IsVisible = false;
                timer.Stop();
            };
            timer.Start();
        }
        
        public void Log(params object[] args)
        {
            string logDirectory = "logs/client_logs";
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            string logFileName = Path.Combine(logDirectory, $"log_{DateTime.Now:yy-MM-dd_HH}.txt");
            string message = string.Join(" ", args);

            Console.WriteLine(message);

            using (StreamWriter writer = new StreamWriter(logFileName, true))
            {
                writer.WriteLine(message + "\n");
            }
        }
        
        public string ExtractUsernameFromTwitterUrl(string url)
        {
            try
            {
                Uri uri = new Uri(url);

                string path = uri.AbsolutePath;

                string[] segments = path.Split('/');

                string username = segments[segments.Length - 1];

                return username;
            }
            catch (UriFormatException)
            {
                return "Invalid URL";
            }
        }
        
        public string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength); 
        }
        
        public async void CloseApp(IControlledApplicationLifetime applicationLifetime)
        {
            try
            {
                GlobalState.clientHelper.Log("Attempting to disconnect from the server.");

                Task.Run(() =>
                {
                    GlobalState.EnqueueResponse("DisconnectCommand");
                    Thread.Sleep(2000);
                }).Wait();

            }
            catch (Exception ex)
            {
                GlobalState.clientHelper.Log($"Error during disconnect: {ex.Message}");
            }
            finally
            {
                GlobalState.clientHelper.Log("Successfully disconnected from the server.");
                applicationLifetime.Shutdown();
            }
        }

        public void PlayWaveResource(string waveResourceName)
        {
            Task.Run(() =>
            {
                Uri uri = new($"avares://ASFS/res/{waveResourceName}.wav");

                using (Stream assetStream = AssetLoader.Open(uri))
                {
                    using (MemoryStream memoryStream = new())
                    {
                        assetStream.CopyTo(memoryStream);

                        memoryStream.Seek(0, 0);

                        using (WaveFileReader waveFileReader = new(memoryStream))
                        {
                            using (WaveOutEvent waveOutEvent = new())
                            {
                                waveOutEvent.Init(waveFileReader);
                                waveOutEvent.Play();

                                while (waveOutEvent.PlaybackState == PlaybackState.Playing)
                                {
                                    Thread.Sleep(10);
                                }
                            }
                        }
                    }
                }
            });
        }
        
        public async Task PlayWaveResourceAsync(string waveResourceName, float volume, bool loop, CancellationToken cancellationToken)
        {
            Uri uri = new($"avares://ASFS/res/{waveResourceName}.wav");

            while (true)
            {
                using Stream assetStream = AssetLoader.Open(uri);
                using MemoryStream memoryStream = new();
                assetStream.CopyTo(memoryStream);

                memoryStream.Seek(0, SeekOrigin.Begin);

                using WaveFileReader waveFileReader = new(memoryStream);
                using WaveOutEvent waveOutEvent = new();

                waveOutEvent.Init(waveFileReader);
                waveOutEvent.Play();
                waveOutEvent.Volume = volume;

                // Используем TaskCompletionSource для асинхронного ожидания завершения воспроизведения
                var tcs = new TaskCompletionSource<bool>();
                waveOutEvent.PlaybackStopped += (s, e) => tcs.TrySetResult(true);

                // Ожидаем завершения воспроизведения или отмену
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(-1, cancellationToken));

                // Проверяем, было ли выполнение прервано
                if (completedTask == tcs.Task)
                {
                    // Если завершилось воспроизведение
                    if (!loop)
                        break;
                }
                else
                {
                    // Если была отмена
                    waveOutEvent.Stop();
                    break;
                }
            }
        }
    }
}