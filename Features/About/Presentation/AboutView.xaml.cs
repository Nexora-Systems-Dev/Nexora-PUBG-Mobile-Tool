using System.Windows.Controls;
using Nexora.UI.Presentation;

namespace Nexora.Features.About.Presentation;

/// <summary>
/// The About page: static product copy plus the version pill. Fully static by
/// design — the only dynamic value is <see cref="AboutViewModel.VersionDisplay"/>,
/// painted once in the constructor. No handlers, no bus, no services.
/// </summary>
public partial class AboutView : UserControl
{
    private readonly AboutViewModel _viewModel;

    public AboutView()
        : this(viewModel: null)
    {
    }

    public AboutView(AboutViewModel? viewModel = null)
    {
        InitializeComponent();

        _viewModel = BuildViewModel(viewModel);
        VersionText.Text = _viewModel.VersionDisplay;
    }

    /// <summary>
    /// The page's version seam. Exposed for tests and for the shell, which
    /// owns no About state of its own.
    /// </summary>
    public AboutViewModel ViewModel => _viewModel;

    private static AboutViewModel BuildViewModel(AboutViewModel? viewModel)
    {
        // XAML constructs this view with the parameterless ctor, so the view
        // takes its ViewModel from the running container when there is one
        // and only falls back to a locally built instance for the designer
        // and for direct construction (see ShellHelper.TryResolveViewModel).
        // The ViewModel is dependency-free, so the fallback is a plain
        // construction.
        if (viewModel is null
            && ShellHelper.TryResolveViewModel(out AboutViewModel? resolved)
            && resolved is not null)
        {
            return resolved;
        }

        return viewModel ?? new AboutViewModel();
    }
}
