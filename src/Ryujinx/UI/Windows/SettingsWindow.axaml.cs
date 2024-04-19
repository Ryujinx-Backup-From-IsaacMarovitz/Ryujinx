using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Views.Settings;
using Ryujinx.HLE.FileSystem;
using Ryujinx.Ava.UI.ViewModels.Settings;
using System;

namespace Ryujinx.Ava.UI.Windows
{
    public partial class SettingsWindow : StyleableWindow
    {
        private SettingsViewModel ViewModel { get; }

        public readonly SettingsUiView UiPage;
        public readonly SettingsInputView InputPage;
        public readonly SettingsHotkeysView HotkeysPage;
        public readonly SettingsSystemView SystemPage;
        public readonly SettingsCPUView CpuPage;
        public readonly SettingsGraphicsView GraphicsPage;
        public readonly SettingsAudioView AudioPage;
        public readonly SettingsNetworkView NetworkPage;
        public readonly SettingsLoggingView LoggingPage;

        public SettingsWindow(VirtualFileSystem virtualFileSystem, ContentManager contentManager)
        {
            Title = $"{LocaleManager.Instance[LocaleKeys.Settings]}";

            AudioPage = new SettingsAudioView();

            ViewModel = new SettingsViewModel(
                virtualFileSystem,
                contentManager,
                AudioPage.ViewModel);

            UiPage = new SettingsUiView(ViewModel);
            InputPage = new SettingsInputView(ViewModel);
            HotkeysPage = new SettingsHotkeysView(ViewModel);
            SystemPage = new SettingsSystemView(ViewModel);
            CpuPage = new SettingsCPUView();
            GraphicsPage = new SettingsGraphicsView();
            NetworkPage = new SettingsNetworkView();
            LoggingPage = new SettingsLoggingView();

            DataContext = ViewModel;

            ViewModel.CloseWindow += Close;
            ViewModel.SaveSettingsEvent += SaveSettings;
            ViewModel.DirtyEvent += UpdateDirtyTitle;
            ViewModel.ToggleButtons += ToggleButtons;

            InitializeComponent();
            Load();
        }

        public SettingsWindow()
        {
            ViewModel = new SettingsViewModel();
            DataContext = ViewModel;

            InitializeComponent();
            Load();
        }

        public void UpdateDirtyTitle(bool isDirty)
        {
            if (!IsInitialized)
            {
                return;
            }

            if (isDirty)
            {
                Title = $"{LocaleManager.Instance[LocaleKeys.Settings]} - {LocaleManager.Instance[LocaleKeys.SettingsDirty]}";
                Apply.IsEnabled = true;
            }
            else
            {
                Title = $"{LocaleManager.Instance[LocaleKeys.Settings]}";
                Apply.IsEnabled = false;
            }
        }

        public void ToggleButtons(bool enable)
        {
            Buttons.IsEnabled = enable;
        }

        public void SaveSettings()
        {
            InputPage.SaveCurrentProfile();

            if (Owner is MainWindow window && ViewModel.DirectoryChanged)
            {
                window.LoadApplications();
            }
        }

        private void Load()
        {
            NavPanel.SelectionChanged += NavPanelOnSelectionChanged;
            NavPanel.SelectedItem = NavPanel.MenuItems.ElementAt(0);
        }

        private void NavPanelOnSelectionChanged(object sender, NavigationViewSelectionChangedEventArgs e)
        {
            if (e.SelectedItem is NavigationViewItem navItem && navItem.Tag is not null)
            {
                switch (navItem.Tag.ToString())
                {
                    case "UiPage":
                        NavPanel.Content = UiPage;
                        break;
                    case "InputPage":
                        NavPanel.Content = InputPage;
                        break;
                    case "HotkeysPage":
                        NavPanel.Content = HotkeysPage;
                        break;
                    case "SystemPage":
                        NavPanel.Content = SystemPage;
                        break;
                    case "CpuPage":
                        NavPanel.Content = CpuPage;
                        break;
                    case "GraphicsPage":
                        NavPanel.Content = GraphicsPage;
                        break;
                    case "AudioPage":
                        NavPanel.Content = AudioPage;
                        break;
                    case "NetworkPage":
                        NavPanel.Content = NetworkPage;
                        break;
                    case "LoggingPage":
                        NavPanel.Content = LoggingPage;
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        private static void RevertIfNotSaved()
        {
            Program.ReloadConfig();
        }

        private async void Cancel_OnClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsModified)
            {
                var result = await ContentDialogHelper.CreateConfirmationDialog(
                    LocaleManager.Instance[LocaleKeys.DialogSettingsUnsavedChangesMessage],
                    LocaleManager.Instance[LocaleKeys.DialogSettingsUnsavedChangesSubMessage],
                    LocaleManager.Instance[LocaleKeys.InputDialogYes],
                    LocaleManager.Instance[LocaleKeys.InputDialogNo],
                    LocaleManager.Instance[LocaleKeys.RyujinxConfirm],
                    parent: this);

                if (result != UserResult.Yes)
                {
                    return;
                }
            }

            RevertIfNotSaved();
            Close();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            HotkeysPage.Dispose();
            InputPage.Dispose();
            base.OnClosing(e);
        }
    }
}
