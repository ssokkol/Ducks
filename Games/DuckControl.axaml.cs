using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace ASFS.UI.Windows.Games;

public partial class DuckControl : UserControl
{
    private Image[] _rightDirectionFrames;
    private Image[] _leftDirectionFrames;
    public Image _shootedDuck;
    public Image _deadDuck;
    private int _currentFrameIndex;
    private bool _isFlyingRight;
    private bool _isAnimating;
    public bool _isShot;

    public DuckControl()
    {
        InitializeComponent();
        
        _rightDirectionFrames = new[]
        {
            this.FindControl<Image>("DuckToRightTopFirst"),
            this.FindControl<Image>("DuckToRightTopSecond"),
            this.FindControl<Image>("DuckToRightTopThird")
        };

        _leftDirectionFrames = new[]
        {
            this.FindControl<Image>("DuckToLeftTopFirst"),
            this.FindControl<Image>("DuckToLeftTopSecond"),
            this.FindControl<Image>("DuckToLeftTopThird")
        };
        
        _shootedDuck = this.FindControl<Image>("ShootedDuck");
        _deadDuck = this.FindControl<Image>("DeadDuck");

        _currentFrameIndex = 0;
        _isFlyingRight = true;
        _isAnimating = false;
        _isShot = false;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public async void StartFlyingAnimation()
    {
        if (_isAnimating || _isShot) return;
        _isAnimating = true;

        while (_isAnimating && !_isShot)
        {
            if (_isFlyingRight)
            {
                _rightDirectionFrames[_currentFrameIndex].IsVisible = false;
            }
            else
            {
                _leftDirectionFrames[_currentFrameIndex].IsVisible = false;
            }

            _currentFrameIndex = (_currentFrameIndex + 1) % 3;

            if (_isFlyingRight)
            {
                _rightDirectionFrames[_currentFrameIndex].IsVisible = true;
            }
            else
            {
                _leftDirectionFrames[_currentFrameIndex].IsVisible = true;
            }

            await Task.Delay(100);
        }
        
        HideAllFrames();
    }

    public void StopFlyingAnimation()
    {
        _isAnimating = false;
        HideAllFrames();

    }

    private async void Duck_Clicked(object sender, RoutedEventArgs e)
    {
        if (_isShot) return;

        _isShot = true;
        _isAnimating = false;
        
        GlobalState.clientHelper.PlayWaveResource("game/audio/shot");
        GlobalState.clientHelper.PlayWaveResource("game/audio/quack");

        HideAllFrames();

        _shootedDuck.IsVisible = true;
        StopFlyingAnimation();
        await SetDuckDead();
        StartFallingAnimation();
    }
    
    private async void StartFallingAnimation()
    {
        await Task.Delay(200);

        _shootedDuck.IsVisible = false;
        _deadDuck.IsVisible = true;
        
        GlobalState.clientHelper.PlayWaveResource("game/audio/falling");

        var canvas = this.FindAncestorOfType<Canvas>();
        double dy = 2 * 5;
        
        var gameWindow = this.FindAncestorOfType<Window>();
        if (gameWindow is HuntGame huntGame)
        {
            huntGame.OnDuckClicked();
        }

        while (Canvas.GetTop(this) < canvas.Bounds.Height - this.Bounds.Height)
        {
            double top = Canvas.GetTop(this) + dy;
            Canvas.SetTop(this, top);
            await Task.Delay(20);
        }

        this.IsVisible = false;
    }
    
    public void ChangeDirection(bool flyRight)
    {
        if (_isShot) return;
        _isFlyingRight = flyRight;
        _currentFrameIndex = 0;

        HideAllFrames();

        if (_isFlyingRight)
        {
            _rightDirectionFrames[_currentFrameIndex].IsVisible = true;
        }
        else
        {
            _leftDirectionFrames[_currentFrameIndex].IsVisible = true;
        }
    }

    public async Task SetDuckDead()
    {
        _isAnimating = false;
        HideAllFrames();
        
        _shootedDuck.IsVisible = true;
        _deadDuck.IsVisible = false;
    }

    private void HideAllFrames()
    {
        foreach (var frame in _rightDirectionFrames)
            frame.IsVisible = false;

        foreach (var frame in _leftDirectionFrames)
            frame.IsVisible = false;
    }
}
