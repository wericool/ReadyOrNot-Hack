using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Diagnostics;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Windows.Forms;
using System.Drawing;
using Microsoft.Win32;

public class Preparation { public bool RestartRequired; public string Message; }
public static class Launcher {
    const string ProxyHash="CF440B9EB8643BB7C434ACFDA696AEE57FD981D185DCA5E57FB8DBB18F8FC1CD";
    const string RuntimeHash="680A026890ABB4D0DF2211251F8DEFC1681A584275F1521DCC0FE30AF480006F";
    const string GameExe="ReadyOrNotSteam-Win64-Shipping.exe";
    public static string Root { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    static string Hash(string path) { using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",""); }
    public static Process RunningGame() { return Process.GetProcessesByName("ReadyOrNotSteam-Win64-Shipping").FirstOrDefault(); }
    public static bool RuntimeLoaded(Process game) {
        try { foreach(ProcessModule module in game.Modules)if(string.Equals(module.ModuleName,"UE4SS.dll",StringComparison.OrdinalIgnoreCase))return true; }catch { }
        return false;
    }
    public static string FindGameDirectory() {
        using(var game=RunningGame()) { if(game!=null) { try { return Path.GetDirectoryName(game.MainModule.FileName); }catch { } } }
        string state=Path.Combine(Root,"game-location.txt");
        if(File.Exists(state)){string saved=File.ReadAllText(state).Trim();if(File.Exists(Path.Combine(saved,GameExe)))return saved;}
        var steamRoots=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var key in new[]{@"HKEY_CURRENT_USER\Software\Valve\Steam",@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"}) {
            foreach(string name in new[]{"SteamPath","InstallPath"}) {var value=Registry.GetValue(key,name,null) as string;if(!string.IsNullOrEmpty(value))steamRoots.Add(value);}
        }
        var libraries=new System.Collections.Generic.HashSet<string>(steamRoots,StringComparer.OrdinalIgnoreCase);
        foreach(string steam in steamRoots) {
            string vdf=Path.Combine(steam,"steamapps","libraryfolders.vdf");
            if(File.Exists(vdf))foreach(Match match in Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s*\"([^\"]+)\""))libraries.Add(match.Groups[1].Value.Replace("\\\\","\\"));
        }
        foreach(string library in libraries) {
            string bin=Path.Combine(library,"steamapps","common","Ready Or Not","ReadyOrNot","Binaries","Win64");
            if(File.Exists(Path.Combine(bin,GameExe)))return bin;
        }
        return null;
    }
    static byte[] Resource(string name) {
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
            if(stream==null)throw new InvalidOperationException("В EXE отсутствует ресурс: "+name);
            using(var memory=new MemoryStream()){stream.CopyTo(memory);return memory.ToArray();}
        }
    }
    static void ExtractRuntime(string bin) {
        using(var memory=new MemoryStream(Resource("RoN.UE4SS.zip")))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read)) {
            string prefix=Path.GetFullPath(bin).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            foreach(var entry in zip.Entries) {
                string destination=Path.GetFullPath(Path.Combine(bin,entry.FullName.Replace('/',Path.DirectorySeparatorChar)));
                if(!destination.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Недопустимый путь в архиве UE4SS.");
                if(string.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(destination);continue;}
                if(File.Exists(destination))throw new IOException("Файл уже существует: "+Path.GetFileName(destination));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using(var input=entry.Open())using(var output=File.Create(destination))input.CopyTo(output);
            }
        }
        File.WriteAllText(Path.Combine(bin,"ue4ss","Mods","mods.txt"),"RoNESP : 1\r\n",Encoding.UTF8);
    }
    static void WriteChanged(string path,string text) {
        if(File.Exists(path)) {
            if(File.ReadAllText(path)==text)return;
            string backup=Path.Combine(Root,"backups","launcher-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
            Directory.CreateDirectory(backup);File.Copy(path,Path.Combine(backup,Path.GetFileName(path)));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path,text,new UTF8Encoding(false));
    }
    public static Preparation Prepare(string bin,bool running,bool loaded) {
        bin=Path.GetFullPath(bin);
        if(!File.Exists(Path.Combine(bin,GameExe)))throw new IOException("Не найден EXE Ready Or Not в выбранной папке.");
        string runtime=Path.Combine(bin,"ue4ss"),proxy=Path.Combine(bin,"dwmapi.dll"),disabled=proxy+".RoNESP-disabled";
        bool fresh=!Directory.Exists(runtime),proxyWasEnabled=File.Exists(proxy);
        if(proxyWasEnabled&&Hash(proxy)!=ProxyHash)throw new IOException("Обнаружена другая dwmapi.dll. Лаунчер её не заменяет.");
        if(!fresh&&(!File.Exists(Path.Combine(runtime,"UE4SS.dll"))||Hash(Path.Combine(runtime,"UE4SS.dll"))!=RuntimeHash))throw new IOException("Обнаружена другая или неполная версия UE4SS. Автоматическая замена остановлена.");
        if(File.Exists(disabled)&&Hash(disabled)!=ProxyHash)throw new IOException("Отключённая DLL не совпадает с загрузчиком пакета.");
        if(fresh&&(proxyWasEnabled||File.Exists(disabled)))throw new IOException("Загрузчик присутствует без полного UE4SS. Требуется исправить установку вручную.");
        string lua=Encoding.UTF8.GetString(Resource("RoN.main.lua")).TrimStart('\uFEFF');
        string telemetry=Path.Combine(Root,"telemetry.json").Replace('\\','/').Replace("'","\\'");
        lua=lua.Replace("__RON_ESP_TELEMETRY_PATH__",telemetry);
        string script=Path.Combine(runtime,"Mods","RoNESP","Scripts","main.lua");
        bool changed=!File.Exists(script)||File.ReadAllText(script)!=lua;
        if(fresh)ExtractRuntime(bin);
        else if(!proxyWasEnabled) {
            if(File.Exists(disabled))File.Move(disabled,proxy);
            else {
                using(var memory=new MemoryStream(Resource("RoN.UE4SS.zip")))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read)) {
                    var entry=zip.GetEntry("dwmapi.dll");if(entry==null)throw new InvalidDataException("В архиве отсутствует загрузчик.");
                    using(var input=entry.Open())using(var output=File.Create(proxy))input.CopyTo(output);
                }
            }
        }
        string ini=File.ReadAllText(Path.Combine(runtime,"UE4SS-settings.ini"));
        bool autoReload=Regex.IsMatch(ini,@"(?m)^\s*EnableAutoReloadingLuaMods\s*=\s*1\s*$");
        string modsPath=Path.Combine(runtime,"Mods","mods.txt");
        string mods=File.Exists(modsPath)?File.ReadAllText(modsPath):"";
        bool enabled=Regex.IsMatch(mods,@"(?m)^\s*RoNESP\s*:\s*1\s*$");
        string updated=Regex.IsMatch(mods,@"(?m)^\s*RoNESP\s*:")?Regex.Replace(mods,@"(?m)^\s*RoNESP\s*:\s*\d+","RoNESP : 1"):mods+"\r\nRoNESP : 1\r\n";
        WriteChanged(script,lua);WriteChanged(modsPath,updated);
        File.WriteAllText(Path.Combine(Root,"game-location.txt"),bin,new UTF8Encoding(false));
        bool restart=running&&(!loaded||!proxyWasEnabled||!enabled||(changed&&!autoReload));
        return new Preparation { RestartRequired=restart,Message=restart?"Файлы готовы. Закрой и заново запусти игру: загрузчик подключается при старте. Лаунчер дождётся новой миссии.":running?"ESP готов.":"Файлы готовы. Запусти игру и загрузи одиночную миссию." };
    }
    public static void Run() {
        string bin=FindGameDirectory();
        if(bin==null) {
            using(var dialog=new OpenFileDialog { Title="Выбери ReadyOrNotSteam-Win64-Shipping.exe",Filter="Ready Or Not|ReadyOrNotSteam-Win64-Shipping.exe",CheckFileExists=true }) {
                if(dialog.ShowDialog()!=DialogResult.OK)return;bin=Path.GetDirectoryName(dialog.FileName);
            }
        }
        Preparation result;
        using(var game=RunningGame())result=Prepare(bin,game!=null,game!=null&&RuntimeLoaded(game));
        if(!result.RestartRequired&&RunningReady()) { Application.Run(new Overlay());return; }
        using(var waiting=new LaunchWindow(result.Message,bin)){Application.Run(waiting);if(waiting.Ready)Application.Run(new Overlay());}
    }
    static bool RunningReady() { using(var game=RunningGame())return game!=null&&RuntimeLoaded(game); }
    class LaunchWindow : Form {
        public bool Ready;readonly Timer timer=new Timer { Interval=1000 };bool mustExit;readonly string bin;
        public LaunchWindow(string message,string gameBin) {
            bin=gameBin;using(var game=RunningGame())mustExit=game!=null;
            Text="Ready Or Not ESP";ClientSize=new Size(560,200);StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
            var label=new Label { Text=message,Location=new Point(22,22),Size=new Size(515,100),Font=new Font("Segoe UI",11) };Controls.Add(label);
            var launch=new Button {Text="Запустить игру",Location=new Point(22,140),Size=new Size(190,36)};
            launch.Click+=(s,e)=>{using(var game=RunningGame()){if(game!=null){label.Text="Игра уже запущена. Если требуется перезапуск, закрой её самостоятельно.";return;}}Process.Start("steam://run/1144200");};Controls.Add(launch);
            var cancel=new Button {Text="Закрыть",Location=new Point(350,140),Size=new Size(190,36)};cancel.Click+=(s,e)=>Close();Controls.Add(cancel);
            timer.Tick+=(s,e)=>{using(var game=RunningGame()){if(game==null){mustExit=false;return;}if(mustExit)return;try{if(!string.Equals(Path.GetDirectoryName(game.MainModule.FileName),bin,StringComparison.OrdinalIgnoreCase))return;}catch{return;}if(RuntimeLoaded(game)){Ready=true;Close();}}};timer.Start();
        }
        protected override void OnFormClosed(FormClosedEventArgs e){timer.Dispose();base.OnFormClosed(e);}
    }
}
