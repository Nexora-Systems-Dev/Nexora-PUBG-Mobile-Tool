using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Nexora.Features.Graphics.Domain;

namespace Nexora.UI.Presentation;

/// <summary>
/// Shell half of the connection paint: the title-bar pill, the sidebar
/// indicator and the status line. The Graphics page paints its own dot,
/// detail line and summary from the same event, so neither half reaches into
/// the other's controls — this presenter owns the shell surfaces, the page
/// owns its.
/// </summary>
public sealed class ShellConnectionPresenter
{
    private readonly FrameworkElement _resourceHost;
    private readonly Shape _topDot;
    private readonly Border _topPill;
    private readonly TextBlock _topText;
    private readonly Shape _sideDot;
    private readonly TextBlock _sideText;
    private readonly TextBlock _adbText;
    private readonly Action<string, bool> _setStatus;

    public ShellConnectionPresenter(
        FrameworkElement resourceHost,
        Shape topDot,
        Border topPill,
        TextBlock topText,
        Shape sideDot,
        TextBlock sideText,
        TextBlock adbText,
        Action<string, bool> setStatus)
    {
        _resourceHost = resourceHost;
        _topDot = topDot;
        _topPill = topPill;
        _topText = topText;
        _sideDot = sideDot;
        _sideText = sideText;
        _adbText = adbText;
        _setStatus = setStatus;
    }

    /// <summary>
    /// Paints the shell surfaces for a connection-state change and forwards
    /// the message to the status line. Wired straight to the Graphics page's
    /// connection event; every branch below is verbatim the pre-extraction
    /// shell paint.
    /// </summary>
    public void Show(ConnectionState state, string message)
    {
        switch (state)
        {
            case ConnectionState.Failed:
                ShowFailure();
                _setStatus(message, true);
                break;
            case ConnectionState.AwaitingVersion:
                // Reachable only when a connect reports success without a
                // transport state. It paints the shell surfaces only — the
                // page's dot stays deliberately untouched — so do not fold it
                // into the success helper.
                ShowSuccess();
                _topText.Text = "GameLoop connected";
                _setStatus(message, false);
                break;
            case ConnectionState.TransportConnected:
            case ConnectionState.FullyConnected:
                ShowSuccess();
                _topText.Text = "Connected to GameLoop";
                _setStatus(message, false);
                break;
            default:
                ShowDisconnected();
                _setStatus(message, false);
                break;
        }
    }

    private void ShowSuccess()
    {
        var success = GetBrush("Success");
        var emeraldGlow = CreateSuccessGlow();
        _topDot.Fill = success;
        _topDot.Effect = emeraldGlow;
        _topPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x10, 0xB9, 0x81));
        _sideDot.Fill = success;
        _sideDot.Effect = emeraldGlow;
        _sideText.Text = "CONNECTED";
        _adbText.Text = "ADB: Connected";
    }

    private void ShowFailure()
    {
        var danger = GetBrush("Danger");
        var dangerGlow = new DropShadowEffect { Color = Color.FromRgb(0xEF, 0x44, 0x44), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.85 };
        _topDot.Fill = danger;
        _topDot.Effect = dangerGlow;
        _topPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xEF, 0x44, 0x44));
        _sideDot.Fill = danger;
        _sideDot.Effect = dangerGlow;
        _topText.Text = "Connection failed";
        _sideText.Text = "FAILED";
    }

    private void ShowDisconnected()
    {
        var muted = GetBrush("TextMuted");
        _topDot.Fill = muted;
        _topDot.Effect = null;
        _topText.Text = "Not connected";
        _topPill.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x32, 0x44));
        _sideDot.Fill = muted;
        _sideDot.Effect = null;
        _sideText.Text = "NOT CONNECTED";
        _adbText.Text = "ADB: Offline";
    }

    private Brush GetBrush(string key) => ShellHelper.GetBrush(_resourceHost, key);

    private static DropShadowEffect CreateSuccessGlow() =>
        new() { Color = Color.FromRgb(0x10, 0xB9, 0x81), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };
}
