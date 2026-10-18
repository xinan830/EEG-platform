# Why

Portable packages currently omit the licensed ANT/eego SDK and retain only a
machine-specific absolute SDK path. A recipient therefore sees “SDK missing”.

# What Changes

- Copy the licensed x64 `eego-SDK.dll` into the portable package's `sdk` folder.
- Let WPF use that bundled file when the saved local path is empty or missing.
- Keep a valid explicitly selected local path higher priority.

# Impact

This changes packaging and local SDK discovery only. It does not change the
vendor ABI, device driver installation, acquisition mapping, or scientific
algorithms. The package still requires the ANT USB driver and compatible device.
