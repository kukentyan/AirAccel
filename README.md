# AirAccel

AirAccel for Minecraft Bedrock Edition (MCBE) 26.30 ~ 26.42.
A tool to customize the air acceleration in Minecraft.

## ✨ Features

- **Automatic Process Monitoring & Reconnection**
  Continuously monitors the Minecraft process in the background. Even if the game is restarted, it will automatically reconnect and reapply your configured multiplier.
- **Complete Stealth (Boss Key)**
  Press the `Right Shift` key to completely hide the app from both the screen and the taskbar. Press it again to bring it back.
- **Optimized Shellcode Injection**
  Designed with safety and optimization in mind. It updates the multiplier without consuming unnecessary memory or causing memory leaks every time you change the value.

## 📖 Usage

1. **Launch**
   Double-click `AirAccel.exe` to start the application.
2. **Apply Multiplier**
   Enter your desired air acceleration multiplier in the console and press `Enter`.
   (Example: `1.02`, `1.5`, etc.)
3. **Game Restarts**
   As long as the application remains open, the last applied multiplier will be automatically injected whenever you restart Minecraft.

## 🛠️ Build

If you want to compile the source code yourself, run the following command in the project directory. It will output a single executable `.exe` file.

```cmd
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

## 💬 Community

Join the official **TorioGhost Client** community for questions, updates, and more!

**Discord Invite:** [discord.gg/xq8sWQhuXG](https://discord.gg/xq8sWQhuXG)

## 📄 License

This project is licensed under the [MIT License](LICENSE).

---
*Made by Ducky*