using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NAudio.Wave;

namespace ASFS.UI.Windows.Games
{
    public partial class HuntGame : Window
    {
        private int _score;
        private int localScore;
        private int _misses;
        private int _round;
        private int _timeRemaining;
        private Random _random;
        private bool _gameStarted;
        private List<DuckControl> _ducks;
        private Image? _currentDog;
        private static readonly TimeSpan DuckLifetime = TimeSpan.FromSeconds(10);
        private static readonly double DuckSpeed = 5.0;

        private CancellationTokenSource _musicCancellationTokenSource;
        private static HuntGame? _instance;
        private CancellationTokenSource _flappingCancellationTokenSource;
        
        public HuntGame()
        {
            InitializeComponent();
            _random = new Random();
            _gameStarted = false;
            _ducks = new List<DuckControl>();
            Uri uri = new Uri("avares://ASFS/res/game/CrossHair.png");
            var newCursor = new Cursor(new Bitmap(AssetLoader.Open(uri)), new PixelPoint(0, 0));
            Cursor = newCursor;

            Closing += OnClosing;
            
            _musicCancellationTokenSource = new CancellationTokenSource();
            Task.Run(() => PlayBackgroundMusic(_musicCancellationTokenSource.Token));
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                foreach (var duck in _ducks)
                {
                    duck._isShot = true;
                    duck.StopFlyingAnimation();
                    duck.IsVisible = false;
                }
            
                _ducks.Clear();
                _gameStarted = false;
                _musicCancellationTokenSource?.Cancel();
                _flappingCancellationTokenSource?.Cancel();
            
                _flappingCancellationTokenSource?.Dispose();
                _musicCancellationTokenSource?.Dispose();
            
                _instance?._musicCancellationTokenSource?.Cancel();
                _instance?._musicCancellationTokenSource?.Dispose();
                _instance?._flappingCancellationTokenSource?.Cancel();
                _instance?._flappingCancellationTokenSource?.Dispose();
                if (_instance != null) _instance._gameStarted = false;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
            }
        }
        
        private async Task PlayBackgroundMusic(CancellationToken token)
        {
            Uri uri = new("avares://ASFS/res/game/audio/bg.wav");

            using (Stream assetStream = AssetLoader.Open(uri))
            using (MemoryStream memoryStream = new())
            {
                await assetStream.CopyToAsync(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);

                using (var audioStream = new MemoryStream(memoryStream.ToArray()))
                using (var waveStream = new WaveFileReader(audioStream))
                using (var waveOut = new WaveOutEvent())
                {
                    waveOut.Init(waveStream);
                    waveOut.Play();
                    waveOut.Volume = 0.3f;

                    while (!token.IsCancellationRequested && waveOut.PlaybackState == PlaybackState.Playing)
                    {
                        await Task.Delay(100, token);
                    }

                    waveOut.Stop();
                }
            }
        }
        
        public static HuntGame GetInstance()
        {
            if (_instance == null || !_instance.IsVisible)
            {
                _instance = new HuntGame();
            }
            else if (_instance.IsVisible)
            {
                _instance.Activate();
            }
            return _instance;
        }
        
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

         private void StartGame_Click(object sender, RoutedEventArgs e)
        {
            var startButton = sender as Button;
            startButton.IsVisible = false;

            _score = 0;
            _misses = 0;
            _round = 1;
            _timeRemaining = 60;

            UpdateScore();
            UpdateMissed();
            UpdateRound();
            UpdateTime();

            _gameStarted = true;
            StartTimer();
            StartDuckFlight();
            _musicCancellationTokenSource.Cancel();
        }

        private void StartTimer()
        {
            DispatcherTimer.Run(() =>
            {
                if (!_gameStarted) return false;

                _timeRemaining--;
                UpdateTime();

                if (_timeRemaining <= 0)
                {
                    EndRound();
                    return false;
                }

                return true;
            }, TimeSpan.FromSeconds(1));
        }

        private void StartDuckFlight()
        {
            if(!_gameStarted) return;
            var canvas = this.FindControl<Canvas>("DuckCanvas");
            var duckCount = _random.Next(2, 4);
            _ducks.Clear();

            var startUpPosition = GetStartPosition(canvas);
            
            for (int i = 0; i < duckCount; i++)
            {
                var duck = new DuckControl();
                canvas.Children.Add(duck);
                _ducks.Add(duck);
                PositionDuck(duck, canvas, startUpPosition);
                duck.ChangeDirection(true);
                duck.StartFlyingAnimation();
                MoveDuck(duck);
            }
            
            // Запуск звука flapping при старте полёта
            _flappingCancellationTokenSource = new CancellationTokenSource();
            _ = Task.Run(() => GlobalState.clientHelper.PlayWaveResourceAsync("game/audio/flapping", 0.1f, true, _flappingCancellationTokenSource.Token));
        }

        private void PositionDuck(DuckControl duck, Canvas canvas, (double, double) startCoordinates)
        {
            double duckX = startCoordinates.Item1 + _random.Next(-50, 50);
            double duckY = startCoordinates.Item2 + _random.Next(0, 50);
            Canvas.SetLeft(duck, duckX);
            Canvas.SetTop(duck, duckY);
        }

        private (double, double) GetStartPosition(Canvas canvas)
        {
            double duckWidth = 75;
            double duckHeight = 75;
            double startX = _random.Next(0, (int)((canvas.Bounds.Width / 2) + duckWidth));
            double startY = _random.Next((int)(canvas.Bounds.Height - duckHeight - 100), (int)(canvas.Bounds.Height - duckHeight));
            return (startX, startY);
        }
        
        private async void MoveDuck(DuckControl duck)
        {
            var canvas = this.FindControl<Canvas>("GameCanvas");
            var startTime = DateTime.UtcNow;

            double dx = DuckSpeed;
            double dy = -DuckSpeed;
            bool hasReachedSafeHeight = false;

            int bounceCount = 0;
            int maxBounces = 5;

            var lastDirectionChangeTime = DateTime.UtcNow;

            bool isFlyingRight = dx > 0;
            duck.ChangeDirection(isFlyingRight);
            duck.StartFlyingAnimation();

            while (DateTime.UtcNow - startTime < DuckLifetime)
            {
                if (duck.IsVisible == false) break;

                double left = Canvas.GetLeft(duck);
                double top = Canvas.GetTop(duck);

                if (!hasReachedSafeHeight)
                {
                    if (top < canvas.Bounds.Height - duck.Bounds.Height - 200)
                    {
                        hasReachedSafeHeight = true;
                        dy = DuckSpeed;
                    }
                }
                else
                {
                    if (DateTime.UtcNow - lastDirectionChangeTime >= TimeSpan.FromSeconds(0.75))
                    {
                        dx = -dx;
                        isFlyingRight = dx > 0;
                        duck.ChangeDirection(isFlyingRight);

                        dy = _random.NextDouble() * (Math.Abs(dy) + DuckSpeed) * Math.Sign(dy);

                        lastDirectionChangeTime = DateTime.UtcNow;
                    }
                }

                double newLeft = left + dx;
                double newTop = top + dy;

                if (newTop >= canvas.Bounds.Height - duck.Bounds.Height)
                {
                    newTop = canvas.Bounds.Height - duck.Bounds.Height;
                    dy = -Math.Abs(dy);
                    bounceCount++;
                }

                if (newLeft <= 0 || newLeft >= canvas.Bounds.Width - duck.Bounds.Width)
                {
                    dx = -dx;
                    isFlyingRight = dx > 0;
                    duck.ChangeDirection(isFlyingRight);
                    bounceCount++;
                }

                if (newTop <= 0)
                {
                    dy = Math.Abs(dy);
                    bounceCount++;
                }

                if (bounceCount >= maxBounces)
                {
                    dx = DuckSpeed;
                    dy = DuckSpeed;
                }

                if (duck._isShot)
                {
                    newTop = top + 3;
                    newLeft = left;
                }

                Canvas.SetLeft(duck, newLeft);
                Canvas.SetTop(duck, newTop);

                await Task.Delay(20);
            }

            duck.IsVisible = false;
            duck.StopFlyingAnimation();
            if (_gameStarted && _ducks.All(d => !d.IsVisible))
            {
                EndRound();
            }
        }

        public async void OnDuckClicked()
        {
            if (!IsGameStarted()) return;
            
            _score++;
            localScore++;
            UpdateScore();

            if (_ducks.All(d => d._isShot))
            {
                _flappingCancellationTokenSource.Cancel();
            }

            if (_ducks.All(d => !d.IsVisible))
            {
                EndRound();
            }
        }


        private void UpdateScore()
        {
            this.FindControl<TextBlock>("ScoreTextBlock").Text = $"Score: {_score}";
        }

        private void UpdateMissed()
        {
            this.FindControl<TextBlock>("MissedTextBlock").Text = $"Missed: {_misses}";
        }

        private void UpdateRound()
        {
            this.FindControl<TextBlock>("RoundTextBlock").Text = $"Round: {_round}";
        }

        private void UpdateTime()
        {
            this.FindControl<TextBlock>("TimeTextBlock").Text = $"Time: {_timeRemaining}";
        }

        private async void EndRound()
        {
            foreach (var duck in _ducks)
            {
                duck._isShot = true;
                duck.StopFlyingAnimation();
                duck.IsVisible = false;
            }
            
            _ducks.Clear();
            
            _round++;
            _timeRemaining = 60;
            UpdateRound();
            UpdateTime();
            await ShowDog(localScore);
            localScore = 0;
            StartDuckFlight();
        }

        private async void GameCanvas_PointerPressed(object? sender, RoutedEventArgs routedEventArgs)
        {
            if (!IsGameStarted()) return;
            GlobalState.clientHelper.PlayWaveResource("game/audio/shot");
            Canvas gameCanvas = this.FindControl<Canvas>("GameCanvas");
            gameCanvas.Background = Brushes.IndianRed;
            await Task.Delay(200);
            gameCanvas.Background = Brushes.LightSkyBlue;
            _misses++;
            UpdateMissed();
            ShowDogLose();
        }

        private bool IsGameStarted()
        {
            var startButton = this.FindControl<Button>("StartGame");
            return startButton.IsVisible == false;
        }

        private async Task ShowDog(int score)
        {
            var dogLose = this.FindControl<Image>("DogLose");
            var dogOne = this.FindControl<Image>("DogOne");
            var dogTwo = this.FindControl<Image>("DogTwo");

            Image dogToShow = dogLose;

            if (score > 1)
            {
                dogToShow = dogTwo;
            }
            else if (score > 0)
            {
                dogToShow = dogOne;
            }

            _musicCancellationTokenSource?.Cancel();
            _flappingCancellationTokenSource?.Cancel();
            await HideCurrentDog(); 

            _currentDog = dogToShow; 
            if(score > 0) GlobalState.clientHelper.PlayWaveResource("game/audio/caught");

            Canvas.SetTop(dogToShow, 450);
            AnimateDog(dogToShow);
            await Task.Delay(2000);

            await HideCurrentDog(); 
        }

        private async void ShowDogLose()
        {
            var dogLose = this.FindControl<Image>("DogLose");

            await HideCurrentDog(); 

            _currentDog = dogLose; 

            Canvas.SetTop(dogLose, 450);
            AnimateDog(dogLose);
            await Task.Delay(2000);

            await HideCurrentDog(); 
        }

        private void AnimateDog(Image dog)
        {
            dog.IsVisible = true;

            double randomTop = _random.Next(265, 270);
            double randomLeft = _random.Next(120, 680);
            Canvas.SetLeft(dog, randomLeft);
            
            dog.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Canvas.TopProperty,
                    Duration = TimeSpan.FromSeconds(0.5),
                    Easing = new SineEaseOut()
                }
            };

            Canvas.SetTop(dog, randomTop);
        }

        private async Task HideCurrentDog()
        {
            if (_currentDog != null && _currentDog.IsVisible)
            {
                Canvas.SetTop(_currentDog, 450);
                _currentDog.IsVisible = false;
                await Task.Delay(100);
            }
        }
    }
}