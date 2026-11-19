using System.Windows;
using MineLauncher.ViewModels;
namespace MineLauncher;
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
}