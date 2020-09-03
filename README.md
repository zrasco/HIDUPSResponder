# HIDUPSResponder
.NET Core 3.1 Console/Windows service that monitors a locally connected UPS (via USB) and allows you to respond to events with custom scripts

- You must install the .NET Core 3.1 Runtime for this to run. At the time of this writing my working environment uses the 3.1.7 runtime.
- Your UPS must be connected with a USB cable and be registered in Device Manager as a "HID UPS battery". You should see the battery in the system tray as though you were on a laptop. Usually, installing other UPS monitoring software will remove that driver and replace it with one of their own, so try uninstalling the other monitoring software if you'd like to make this work.
- You can easily use a laptop as a testing environment for deploying this program, as it will respond to you unplugging and replugging in the A/C adapter in the same fashion as a UPS losing power and regaining power.
