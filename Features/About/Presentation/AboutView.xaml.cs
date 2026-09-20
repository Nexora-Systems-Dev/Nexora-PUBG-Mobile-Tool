using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;

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
        // and for direct construction. The ViewModel is dependency-free, so
        // the fallback is a plain construction.
        if (viewModel is null
            && System.Windows.Application.Current is App && App.Services is IServiceProvider services)
        {
            if (services.GetService<AboutViewModel>() is { } resolved) return resolved;
        }

        return viewModel ?? new AboutViewModel();
    }
}
