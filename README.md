# HIDUPSResponder
.NET Core 3.1 Console/Windows service that monitors a locally connected UPS (via USB) and allows you to respond to events with custom scripts. Includes a WiX installer.

Credit goes to CodeProject user klinkenbecker for providing the Win32 Power Management C# classes. Source: https://www.codeproject.com/Articles/292725/Using-WM-POWER-events-to-monitor-a-UPS

- Your UPS must be connected with a USB cable and be registered in Device Manager as a "HID UPS battery". You should see the battery in the system tray as though you were on a laptop. Usually, installing other UPS monitoring software will remove that driver and replace it with one of their own, so try uninstalling the other monitoring software if you'd like to make this work.
- You can easily use a laptop as a testing environment for deploying this program, as it will respond to you unplugging and replugging in the A/C adapter in the same fashion as a UPS losing power and regaining power.
- WiX project will build a standalone or framework-dependent installer, depending on the configuration you build against. Use Release for the framework-dependent version, and Release_Static for the standalone version.
- You can install and uninstall this program as a Windows service once you've installed the MSI by using the batch files winsvc_install.bat and winsvc_uninstall.bat . Note that you'll need to manually uninstall the service before uninstalling the application; this will be fixed in a later release.
