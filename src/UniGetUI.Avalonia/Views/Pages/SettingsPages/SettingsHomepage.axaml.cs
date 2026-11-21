using Avalonia.Controls;
using UniGetUI.Avalonia.ViewModels.Pages.SettingsPages;
using UniGetUI.Core.Tools;

namespace UniGetUI.Avalonia.Views.Pages.SettingsPages;

public sealed partial class SettingsHomepage : UserControl, ISettingsPage
{
    public bool CanGoBack => false;
    public string ShortTitle => CoreTools.Translate("UniGetUI Settings");

    public event EventHandler? RestartRequired { add { } remove { } }
    public event EventHandler<Type>? NavigationRequested;

    public SettingsHomepage()
    {
        DataContext = new SettingsHomepageViewModel();
        InitializeComponent();

        var vm = (SettingsHomepageViewModel)DataContext;
        vm.NavigationRequested += (s, t) => NavigationRequested?.Invoke(s, t);

        ExperimentalButton.UnderText = CoreTools.Translate("Beta features and other options that shouldn't be touched");

        ApplyDeveloperMode();
    }

    /// <summary>未打开「开发人员选项」时，隐藏普通用户用不到的高级设置入口。</summary>
    private void ApplyDeveloperMode()
    {
        bool developerMode = global::UniGetUI.Core.Settings.Settings.Get(
            global::UniGetUI.Core.Settings.Settings.K.DeveloperMode);

        foreach (
            Control button in new Control[]
            {
                AdministratorButton,
                ManagersButton,
                ExtensionsButton,
                InternetButton,
                ExperimentalButton,
            }
        )
        {
            button.IsVisible = developerMode;
        }
    }
}
