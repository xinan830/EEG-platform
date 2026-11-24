using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Devices.Views;

/// <summary>Owner-centred precondition dialog for creating a channel configuration.</summary>
public partial class DeviceSelectionDialog : Window
{
    public DeviceSelectionDialog(IEnumerable<AcquisitionDeviceDescriptor> devices)
    {
        InitializeComponent();
        Devices = devices.ToArray();
        DataContext = this;
        EmptyHint.Visibility = Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public IReadOnlyList<AcquisitionDeviceDescriptor> Devices { get; }

    public AcquisitionDeviceDescriptor? SelectedDevice => DeviceComboBox.SelectedItem as AcquisitionDeviceDescriptor;

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ConfirmButton.IsEnabled = SelectedDevice is not null;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is not null)
        {
            DialogResult = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
