using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Facet;
using Facet.Core;
using Facet.ChatPlugin;
using ChatStore=Facet.ChatPlugin.ChatStore;
using AgentChatReference=Facet.ChatPlugin.AgentChatReference;
using Facet.Plugins;

// Rules that moved out of Core with the chat plugin, and the plugin's own store, settings and registration (ADR 0084).
internal static partial class Program
{
    internal sealed class ChatPluginSettings : IPluginSettings
    {
        internal readonly Dictionary<string,string> Values=[];
        public string Get(string key)=>Values.GetValueOrDefault(key,"");
        public double GetNumber(string key)=>0;
        public IReadOnlyList<string> GetList(string key)=>[];
        public bool Save(string key,string value){Values[key]=value;return true;}
        public event Action<string>? Changed;
        internal void Raise(string key)=>Changed?.Invoke(key);
    }

    internal sealed class ChatPluginStore : IPluginStore
    {
        internal string? Text;internal bool Fail;
        public string? Read()=>Text;
        public bool Write(string text){if(Fail)return false;Text=text;return true;}
    }

    internal sealed class ChatPluginShell(string root) : IPluginShell
    {
        internal bool Hidden=true,Shown=true,SettingsOpened;internal readonly List<string> Errors=[];internal string? Refusal;internal string? Hotkey;
        public string DataRoot=>root;public string? AgentToolServer=>null;public bool Privacy=>false;
        public bool HideOverlay()=>Hidden;public bool ShowWidget(string widgetId)=>Shown;public void OpenSettings()=>SettingsOpened=true;
        public void ReportError(string message)=>Errors.Add(message);
        public string? SetHotkey(string id,string shortcut){if(Refusal==null)Hotkey=shortcut;return Refusal;}
    }

    internal sealed class ChatPluginRegistry(ChatPluginSettings settings,IPluginStore store,IPluginShell shell,string data) : IWidgetRegistry
    {
        internal readonly List<WidgetDescriptor> Widgets=[];internal readonly List<PluginAgentTool> Tools=[];internal PluginSettingsSection? Section;internal readonly List<LauncherMode> Modes=[];internal readonly List<PluginHotkey> Hotkeys=[];internal readonly List<string> Tray=[];
        public string Directory=>"";public string DataDirectory=>data;public IPluginSettings Settings=>settings;public IPluginStore Store=>store;public IPluginShell Shell=>shell;
        public void AddWidget(WidgetDescriptor widget)=>Widgets.Add(widget);public void AddAgentTool(PluginAgentTool tool)=>Tools.Add(tool);public void AddSettings(PluginSettingsSection section)=>Section=section;
        public void AddLauncherMode(LauncherMode mode)=>Modes.Add(mode);public void AddHotkey(PluginHotkey hotkey)=>Hotkeys.Add(hotkey);public void AddTrayCommand(string label,Action run)=>Tray.Add(label);
    }

    // One running plugin over synthetic settings and a memory store; nothing leaves its folder.
    internal sealed class ChatHarness : IDisposable
    {
        internal readonly ChatPluginSettings Settings=new();internal readonly ChatPluginStore Store=new();internal readonly ChatPluginShell Shell;internal readonly ChatPluginRegistry Registry;internal readonly ChatPlugin Plugin=new();
        internal readonly string Root;
        internal ChatRuntime Runtime=>Plugin.Runtime!;
        internal ChatHarness(string qa,string name)
        {
            Root=Path.Combine(qa,name+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
            Shell=new(Root);Registry=new(Settings,Store,Shell,Directory.CreateDirectory(Path.Combine(Root,"plugin-data","facet.chat")).FullName);
        }
        internal ChatHarness Start(){Plugin.Register(Registry);return this;}
        public void Dispose()=>Plugin.Dispose();
    }

    private static void Check1(bool ok)=>Assert(ok,"chat rule");

    private static void CheckChatRules()
    {
        // conversation archive splits sessions at five minutes and indexes summaries by keyword topic and date
        {
    var start=new DateTimeOffset(2026,10,31,14,3,0,TimeSpan.FromHours(9));
            Check1(ConversationArchive.Continues(start,start.AddMinutes(4.9)) && !ConversationArchive.Continues(start,start.AddMinutes(5)) && !ConversationArchive.Continues(null,start));
    var r=new ConversationRecord{Id=ConversationArchive.NewId(start,"AB12-cd34"),ThreadId="AB12-cd34",Start=start,End=start,Title="첫 질문",TurnIds=["t1"]};
            Check1(r.Id=="2026-10-31_1403_ab12cd" && r.Path=="sessions/2026/10/2026-10-31_1403_ab12cd.md");
    ConversationArchive.ApplySummary(r,"요약입니다\n{\"title\":\"수면 상담\",\"summary\":\"수면 시간을 정리했다.\",\"keywords\":[\"수면\",\"카페인\",\"수면\"],\"topics\":[\"건강\"]}");
            Check1(r.Summarized && r.Title=="수면 상담" && r.Keywords.SequenceEqual(new[]{"수면","카페인"}) && r.Topics.Single()=="건강");
    string doc=ConversationArchive.RenderSession(r,ConversationArchive.RenderTurn(new(start,"잠을 못 잤어","기록해 둘게요.")));
    var parsed=ConversationArchive.ParseFrontMatter(doc)!;
            Check1(parsed.Title=="수면 상담" && parsed.TurnIds.Single()=="t1" && parsed.Start==start && ConversationArchive.Body(doc).Contains("### 14:03 · 나"));
            Check1(ConversationArchive.RenderSession(parsed,ConversationArchive.Body(doc)).Split("# 수면 상담").Length==2);
    var other=new ConversationRecord{Id="b",Start=start.AddDays(-40),Title="음악",Keywords=["재생목록"],Topics=["음악"]};
            Check1(ConversationArchive.Search([r,other],"카페인",null,null,10).Single()==r && ConversationArchive.Search([r,other],"",new DateOnly(2026,10,1),null,10).Single()==r);
            Check1(ConversationArchive.Search([r,other],"수면 재생목록",null,null,10).Count==0);
            Check1(ConversationArchive.RenderMonth("2026-10",[r]).Contains("(../sessions/2026/10/") && ConversationArchive.RenderIndex([r,other]).Contains("topics/건강.md"));
        }
        // voice requests ask for a speech summary only in spoken mode and split it from the chat answer
        {
            Check1(!VoiceReply.Prompt(" 내일 일정 ",false).Contains("<speech>") && VoiceReply.Prompt("내일 일정",true).Contains("<speech>") && VoiceReply.Prompt("내일 일정",true).StartsWith("내일 일정\n"));
    bool rejected=false;try{VoiceReply.Prompt("  ",true);}catch(ArgumentException){rejected=true;}Check1(rejected);
    var split=VoiceReply.Split("## 일정\n- **10시** 회의 [링크](https://a.b)\n\n<speech>내일은 10시에 **회의**가 있어요.</speech>");
            Check1(split.Display=="## 일정\n- **10시** 회의 [링크](https://a.b)" && split.Speech=="내일은 10시에 회의가 있어요.");
    var fallback=VoiceReply.Split("**완료**했습니다. 자세히는 https://x.y 참고");
            Check1(fallback.Display.StartsWith("**완료**") && fallback.Speech=="완료했습니다. 자세히는 참고");
            Check1(VoiceReply.Split(new string('가',900)).Speech.Length==VoiceReply.SpeechLimit+1);
        }
    }

    private static void CheckChatPlugin(string qa)
    {
        CheckChatRules();
        using(var h=new ChatHarness(qa,"chat-plugin").Start()){
            Assert(h.Registry.Widgets.Single().Id=="chat" && WidgetIds.Of(LegacyChat.Plugin,"chat")==LegacyChat.Widget,"the widget id matches the one placed on old layouts");
            Assert(h.Registry.Modes.Single().Id=="chat" && h.Registry.Hotkeys.Single().Shortcut=="Win+Alt+V" && h.Registry.Tray.Single()=="음성 에이전트","launcher, hotkey and tray command register through the plugin contract");
            Assert(h.Registry.Tools.Select(t=>t.Name).SequenceEqual(["facet_search_conversations","facet_read_conversation"]),"the conversation tools come from the plugin");
            var groups=h.Registry.Section!.Fields.Select(f=>f.Group).Distinct().ToArray();
            Assert(groups.SequenceEqual(["에이전트 연결","대화 기록","음성 에이전트"]),"the settings keep the three old groups inside the plugin");
            foreach(string key in new[]{"agent","codexEnabled","codexContextThread","claudeEnabled","claudeModel","voiceHotkey","voiceMicrophone","voiceReply","whisperServer","whisperModel","voiceSpeech"})
                Assert(h.Registry.Section.Fields.Any(f=>f.Key==key),"the setting the move writes has a field: "+key);
            var hotkey=(TextSetting)h.Registry.Section.Fields.Single(f=>f.Key==ChatSettings.HotkeyKey);
            h.Shell.Refusal="겹칩니다";Assert(hotkey.Check!("Win+Alt+B")=="겹칩니다" && h.Shell.Hotkey==null,"a refused voice shortcut reports why");
            h.Shell.Refusal=null;Assert(hotkey.Check!(" Win+Alt+B ")==null && h.Shell.Hotkey=="Win+Alt+B","an accepted voice shortcut rebinds at once");
            var context=(TextSetting)h.Registry.Section.Fields.Single(f=>f.Key==ChatSettings.ContextKey);
            Assert(context.Check!("")==null && context.Check!("아무말")!=null,"a bad Codex thread link is refused before saving");

            string id=Guid.NewGuid().ToString();
            var reference=new AgentChatReference{ThreadId=id,Title="저장 확인"};
            Assert(h.Runtime.SaveChats(chats=>chats.Insert(0,reference)) && h.Runtime.Chats.Single().ThreadId==id && h.Store.Text!.Contains(id),"chat references save to the plugin's store");
            h.Store.Fail=true;Assert(!h.Runtime.SaveChats(chats=>chats.Clear()) && h.Runtime.Chats.Count==1,"a failed store write keeps the current list");h.Store.Fail=false;
            Assert(new ChatStore(h.Store).Load().Single().Title=="저장 확인","a new run reads the saved references");
            h.Store.Text="{\"SchemaVersion\":1,\"Unknown\":3,\"Chats\":[]}";var kept=new ChatStore(h.Store);kept.Load();kept.Save([reference]);
            Assert(h.Store.Text.Contains("\"Unknown\":3"),"fields the store does not know survive a save");

            var now=DateTimeOffset.Now;var record=new ConversationRecord{Id=ConversationArchive.NewId(now,id),ThreadId=id,Start=now,End=now,Title="검색 대상",Keywords=["카페인"]};
            h.Runtime.Chat.Archive.Append(record,"t1",new(now,"질문","답변"));
            Assert(File.Exists(Path.Combine(h.Runtime.ArchiveRoot,"index.md")) && h.Runtime.ArchiveRoot.StartsWith(Path.Combine(h.Root,"plugin-data")),"archive documents live under the plugin's data folder");
            string found=h.Registry.Tools[0].Run("{\"query\":\"카페인\"}").Result;
            Assert(JsonDocument.Parse(found).RootElement.EnumerateArray().Single().GetProperty("id").GetString()==record.Id,"the search tool finds a document");
            Assert(JsonDocument.Parse(h.Registry.Tools[1].Run("{\"id\":\""+record.Id+"\"}").Result).RootElement.GetString()!.Contains("질문"),"the read tool returns the document as JSON text");
            bool bad=false;try{h.Registry.Tools[0].Run("{\"from\":\"2026/10/24\"}").GetAwaiter().GetResult();}catch(ArgumentException){bad=true;}
            Assert(bad,"a bad date is a bad request");
            h.Shell.Shown=false;h.Runtime.OpenChat(null);
            Assert(h.Shell.Errors.Single().Contains("채팅 위젯"),"opening chat without a placed widget says what to do");
        }
        using(var moved=new ChatHarness(qa,"chat-archive-move")){
            string old=Path.Combine(moved.Root,"conversations");Directory.CreateDirectory(old);File.WriteAllText(Path.Combine(old,"index.md"),"옛 기록");
            moved.Start();
            Assert(!Directory.Exists(old) && File.ReadAllText(Path.Combine(moved.Runtime.ArchiveRoot,"index.md"))=="옛 기록","the old archive folder moves into the plugin's data once");
        }
        using(var both=new ChatHarness(qa,"chat-archive-keep")){
            string old=Path.Combine(both.Root,"conversations"),current=Path.Combine(both.Registry.DataDirectory,"conversations");Directory.CreateDirectory(old);Directory.CreateDirectory(current);
            File.WriteAllText(Path.Combine(old,"a.md"),"옛");File.WriteAllText(Path.Combine(current,"a.md"),"새");both.Start();
            Assert(File.ReadAllText(Path.Combine(current,"a.md"))=="새" && File.Exists(Path.Combine(old,"a.md")),"an existing archive folder is never overwritten");
        }
        using(var broken=new ChatHarness(qa,"chat-broken-store")){
            broken.Store.Text="{깨진";bool stopped=false;try{broken.Start();}catch(JsonException){stopped=true;}
            Assert(stopped && broken.Store.Text=="{깨진","an unreadable store stops the plugin and stays untouched");
        }
        using(var list=new ChatHarness(qa,"chat-removed").Start()){
            var row=new RemovedChatList(list.Runtime).Row(new ConversationRecord{Id="qa",Title="짧은 제목"});
            row.Measure(new(420,100));row.Arrange(new(0,0,420,row.DesiredSize.Height));row.UpdateLayout();
            var buttons=row.Children.OfType<Button>().ToArray();
            var title=VisualElements(buttons[0]).OfType<TextBlock>().FirstOrDefault(text=>text.Text=="짧은 제목");
            Assert(title!=null && title.TranslatePoint(new(),row).X<8,"removed chat title renders left-aligned instead of the panel's type name");
            Assert(buttons.Skip(1).All(button=>button.ContentTemplate==null && VisualElements(button).OfType<System.Windows.Shapes.Path>().Any(icon=>icon.ActualWidth>0)),"removed chat actions render vector icons");
        }
        CheckChatMove(qa);
        CheckWhisperStatus(qa);
    }

    // Deployment check (--chat-plugin-folder <path>): the published folder loads in its own context, its native ONNX
    // DLLs resolve there, and the real settings card renders. Runs after build.ps1 has published the plugin.
    private static void CheckDeployedChatPlugin(string folder,string qa)
    {
        string root=Path.Combine(qa,"chat-deployed-"+Guid.NewGuid().ToString("N")),target=Path.Combine(root,"plugins","chat");
        Directory.CreateDirectory(target);
        foreach(string file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories)){
            string copy=Path.Combine(target,Path.GetRelativePath(folder,file));Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.Copy(file,copy);
        }
        Assert(!File.Exists(Path.Combine(target,"Facet.Plugins.dll")) && File.Exists(Path.Combine(target,"onnxruntime.dll")) && File.Exists(Path.Combine(target,"DirectML.dll")),"the folder ships its native runtime and not the contract");
        using var host=new PluginHost(root);
        var manifest=host.Discover().Single();
        Assert(manifest.Id==LegacyChat.Plugin,"the deployed manifest names the id the move writes to");
        var (widgets,section)=host.Load(manifest,new ChatPluginSettings());
        Assert(host.LauncherModes.Count==1 && host.Hotkeys.Single().Hotkey.Shortcut=="Win+Alt+V" && host.TrayCommands.Single().Label=="음성 에이전트" && host.AgentTools.Count==2,"the real host collects everything the plugin offers");
        var view=widgets.Single().Create(new("chat",target));
        var assembly=view.GetType().Assembly;
        Assert(System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assembly)!=System.Runtime.Loader.AssemblyLoadContext.Default,"the plugin runs in its own load context");
        var card=(FrameworkElement)section!.Fields.OfType<CustomSetting>().Single(f=>f.Key=="whisperStatus").Create();
        card.Width=460;card.Measure(new(460,400));card.Arrange(new(0,0,460,card.DesiredSize.Height));card.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(460,Math.Max(1,(int)card.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(card);
        var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using(var output=File.Create(Path.Combine(qa,"whisper-status.png")))png.Save(output);
        var speech=assembly.GetType("Facet.ChatPlugin.SupertonicSpeech")!;
        var models=(string)speech.GetProperty("DefaultFolder",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        if(Directory.Exists(models)){
            using var engine=(IDisposable)Activator.CreateInstance(speech,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,[models,false],null)!;
            var samples=(float[])speech.GetMethod("Synthesize",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(engine,["안녕하세요.","F1"])!;
            Assert(samples.Length>0,"ONNX Runtime loads from the plugin's folder and synthesizes speech");
        }
        File.WriteAllText(Path.Combine(qa,"chat-deployed-result.txt"),"PASS "+target);
    }

    private static void CheckChatMove(string qa)
    {
        string root=Path.Combine(qa,"chat-store-move-"+Guid.NewGuid().ToString("N"));
        var store=new Store(root);string id=Guid.NewGuid().ToString();
        store.Save(new AppData{AgentChatReferences=[new(){ThreadId=id,HostId="claude",Title="옮길 채팅"}]});
        var loaded=new Store(root).Load();
        Assert(loaded.AgentChatReferences.Count==0,"chat references leave records.dat");
        string text=PluginFileStore.For(root,LegacyChat.Plugin).Read()!;
        var chat=new ChatStore(PluginFileStore.For(root,LegacyChat.Plugin)).Load().Single();
        Assert(chat.ThreadId==id && chat.HostId=="claude" && text.Contains("SchemaVersion"),"the plugin reads what the app moved");
        Assert(Directory.GetFiles(Path.Combine(root,"backups"),"records-before-chat-store-*").Length==1,"the move keeps a backup of the records");
        Assert(new Store(root).Load().AgentChatReferences.Count==0 && Directory.GetFiles(Path.Combine(root,"backups"),"records-before-chat-store-*").Length==1,"the move happens once");
    }

    private static void CheckWhisperStatus(string qa)
    {
        using var h=new ChatHarness(qa,"whisper-status").Start();
        string folder=Directory.CreateDirectory(Path.Combine(h.Root,"whisper")).FullName,server=Path.Combine(folder,"whisper-server.exe"),model=Path.Combine(folder,"ggml-test.bin");
        h.Settings.Values[ChatSettings.WhisperServerKey]=server;h.Settings.Values[ChatSettings.WhisperModelKey]=model;
        var view=new WhisperStatusView(h.Runtime);
        Assert(view.Headline=="설치 필요" && view.Detail.Contains("whisper-server.exe") && !view.CanLoad,"a missing server says what is missing and cannot load");
        Assert(view.FileLines.First().StartsWith("✕") && view.FileLines.First().Contains(server),"a missing file shows the path it was expected at");
        File.WriteAllText(server,"");view.Update();
        Assert(view.Headline=="설치 필요" && view.Detail.Contains("모델") && view.FileLines.First().StartsWith("✓") && !view.FileLines.First().Contains(folder),"a found server shows only its name and the model is next");
        File.WriteAllBytes(model,new byte[2097152]);view.Update();
        Assert(view.Headline=="대기 중" && view.CanLoad && view.FileLines.Last().Contains("2 MB"),"all files found waits for the first request and may load now");
        view.Measure(new(420,600));view.Arrange(new(0,0,420,view.DesiredSize.Height));
        Assert(view.DesiredSize.Height<240,"the status card stays compact");
        File.Delete(server);h.Settings.Raise(ChatSettings.WhisperServerKey);view.Update();
        Assert(view.Headline=="설치 필요","the card follows the files");
        // A voice request stops before recording, with a way to the settings.
        h.Runtime.Voice.Toggle();Pump(60);
        var card=h.Runtime.Voice.Window!;
        Assert(card.Stage==VoiceStage.Error && card.Caption.Contains("설정 열기") && !h.Runtime.Voice.Busy,"missing files stop a voice request before it records");
        var border=(Border)((StackPanel)card.Content).Children[0];
        border.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});
        Assert(h.Shell.SettingsOpened && !card.IsVisible,"clicking the error opens the plugin's settings");
        view.Dispose();
    }
}
