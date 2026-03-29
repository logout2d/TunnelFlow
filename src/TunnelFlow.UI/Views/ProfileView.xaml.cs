using System.Windows;
using System.Windows.Controls;
using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.UI.Views;

public partial class ProfileView : UserControl
{
    public ProfileView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is ProfileViewModel vm)
            UserIdBox.Password = vm.UserId;
    }

    private void UserIdBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProfileViewModel vm)
            vm.UserId = UserIdBox.Password;
    }
}
