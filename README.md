# Serial Terminal

A free, open-source serial port (COM) terminal for Windows. It is built for everyday work with microcontrollers such as STM32, Arduino and ESP32.

It was written because the existing tools were either paid, hard to read, or limited, for example by a fixed line length or by not being able to log to a file and watch the output at the same time.

## Features

**Display**
- No line-length limit. Word wrap is optional, with horizontal scrolling when it is off.
- Large, readable monospace font. Zoom with **Ctrl + mouse wheel**.
- Dark and light themes.
- Separate colors for received data (RX), sent data (TX), info messages and errors.
- Optional timestamps with millisecond precision.
- Three display modes: **Text**, **Hex** and **Hex + ASCII**. The Hex modes start a new line after an idle gap, so packets such as Modbus frames show up on separate lines.
- Control characters stay visible (␀ ␛ …) instead of disappearing.
- A buffer of 100,000 lines. Auto-scroll pauses when you scroll up and resumes when you scroll back to the bottom.
- Select lines and press **Ctrl + C** to copy them.

**Logging**
- **Start log** writes to a file while you keep monitoring.
- File names are generated automatically, for example `COM5_2026-09-24_14-30-00.log`. You choose the folder.
- **Save as...** saves everything currently on screen.

**Connection**
- Port list with device names, for example "STMicroelectronics Virtual COM Port". It updates automatically when USB devices are plugged in or removed.
- Any baud rate, including custom values. Data bits, parity, stop bits and flow control are configurable.
- Manual DTR and RTS control.
- **Auto-reconnect** after the board is reset or the cable is unplugged.

**Sending**
- Two independent send lines, each with its own text or HEX mode and line ending (None, CR, LF or CRLF).
- The text stays in the box after sending. **Enter** sends, **Up/Down** browses the history.
- 8 macro buttons: left-click sends, right-click edits.
- HEX input accepts `01 03 00 0A`, `01030A` or `0x01,0x03`.

All settings are saved automatically to `%AppData%\SerialTerminal\settings.json`.

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source

## Build and run

```
git clone <repository-url>
cd <repository-folder>
dotnet run
```

### Standalone .exe

This creates a single `SerialTerminal.exe` that runs without installing .NET:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The output is in `bin\Release\net10.0-windows\win-x64\publish\`.

## Project structure

| Path | Purpose |
|---|---|
| `Core/SerialConnection.cs` | Opening the port and the background read loop |
| `Core/LineAssembler.cs` | Turns raw bytes into display lines (Text/Hex) |
| `Core/PortEnumerator.cs` | Port list with device descriptions (WMI) |
| `Core/LogWriter.cs` | Logging to file |
| `Core/AppSettings.cs` | Settings persisted to JSON |
| `MainViewModel.cs` | Application logic (MVVM) |
| `MainWindow.xaml` | User interface (WPF, Fluent theme) |

## Contributing

Bug reports, ideas and pull requests are welcome.

## Support the project

This program is free. If it saves you time and you would like to support further development, you can make a donation via **PayPal** to:

**kod447@gmail.com**

Thank you!

## License

[MIT](LICENSE): free to use, modify and distribute, including commercially.
