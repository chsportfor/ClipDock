using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ClipDock {
 public class ProjectData {
  public int Version=4;
  public string SourcePath,AbsoluteSourcePath,FpsText="원본 유지";
  public Settings Settings;
  public List<Segment> Clips;
  public double Position,ViewStart,ViewSpan;
  public int Selection;
 }
 public static class Projects {
  public static bool Finite(double d){return !double.IsNaN(d)&&!double.IsInfinity(d);}
  public static void Validate(ProjectData p,double duration){
   if(p==null||(p.Version!=1&&p.Version!=2&&p.Version!=3&&p.Version!=4)||string.IsNullOrWhiteSpace(p.SourcePath)||p.Settings==null||p.Clips==null||p.Clips.Count>500)throw new Exception("지원하지 않거나 손상된 작업 파일입니다.");
   if(p.Clips.Any(c=>c==null||!Finite(c.Start)||!Finite(c.End)||c.Start<0||c.End<=c.Start||c.End>duration+.001))throw new Exception("작업의 편집 구간이 원본 영상 길이와 맞지 않습니다.");
   foreach(var group in p.Clips.Where(c=>c.Parts!=null)){
    if(group.Parts.Count<2||group.Parts.Count>500||group.Parts.Any(c=>c==null||c.Parts!=null||!Finite(c.Start)||!Finite(c.End)||c.Start<0||c.End<=c.Start||c.End>duration+.001))throw new Exception("연결한 구간 정보가 올바르지 않습니다.");
    if(Math.Abs(group.Start-group.Parts.Min(c=>c.Start))>.000001||Math.Abs(group.End-group.Parts.Max(c=>c.End))>.000001)throw new Exception("연결한 구간의 범위가 올바르지 않습니다.");
   }
   if(Segment.Flatten(p.Clips).Count>500)throw new Exception("구간이 너무 많습니다.");
   Arrangement.Validate(p.Clips);var s=p.Settings;
   if(!new[]{"원본","16:9","9:16","4:3","1:1"}.Contains(s.Ratio)||!new[]{"여백","채우기","늘림"}.Contains(s.Mode)||!new[]{"원본 크기 기준","720p","1080p","1440p"}.Contains(s.Resolution)||!new[]{"CPU","NVIDIA"}.Contains(s.Encoder)||!Finite(s.Fps)||s.Fps<1||s.Fps>1000||!Finite(s.X)||!Finite(s.Y)||s.X<0||s.X>1||s.Y<0||s.Y>1)throw new Exception("작업 파일의 저장 설정이 올바르지 않습니다.");
   Engine.ValidateTransform(s);double fps;if(p.FpsText!="원본 유지"&&(!double.TryParse(p.FpsText,System.Globalization.NumberStyles.Float,Engine.Inv,out fps)||!Finite(fps)||fps<1||fps>1000))throw new Exception("작업 파일의 FPS 값이 올바르지 않습니다.");
   if(!Finite(p.Position)||!Finite(p.ViewStart)||!Finite(p.ViewSpan)||p.Position<0||p.Position>duration+.001||p.ViewStart<0||p.ViewSpan<0||p.ViewStart>duration||p.ViewSpan>duration+.001)throw new Exception("작업 파일의 재생 위치가 올바르지 않습니다.");
  }
  public static ProjectData Read(string path){if(new FileInfo(path).Length>2*1024*1024)throw new Exception("작업 파일이 너무 큽니다.");var p=new JavaScriptSerializer().Deserialize<ProjectData>(File.ReadAllText(path,Encoding.UTF8));Validate(p,double.MaxValue);return p;}
  public static string ResolveSource(ProjectData p,string path){string relative=Path.IsPathRooted(p.SourcePath)?p.SourcePath:Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path),p.SourcePath));if(File.Exists(relative))return relative;return !string.IsNullOrEmpty(p.AbsoluteSourcePath)&&File.Exists(p.AbsoluteSourcePath)?p.AbsoluteSourcePath:null;}
  public static void Write(string path,ProjectData p){
   Validate(p,double.MaxValue);if(!string.Equals(Path.GetExtension(path),".clipdock",StringComparison.OrdinalIgnoreCase))throw new Exception("작업 파일은 .clipdock 확장자로 저장하세요.");
   string absolute=Path.GetFullPath(p.SourcePath);p.AbsoluteSourcePath=absolute;
   var directory=new Uri(Path.GetDirectoryName(Path.GetFullPath(path))+Path.DirectorySeparatorChar);var relative=directory.MakeRelativeUri(new Uri(absolute));p.SourcePath=relative.IsAbsoluteUri?absolute:Uri.UnescapeDataString(relative.ToString()).Replace('/',Path.DirectorySeparatorChar);
   string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllText(temp,new JavaScriptSerializer().Serialize(p),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
  }
 }
 public class ThumbnailFrame {public double Start,End;public ImageSource Image;}
 public static class MediaJobs {
  public static Task<byte[]> Frame(string path,double time,CancellationToken token){return Task.Run(()=>{
   token.ThrowIfCancellationRequested();using(var p=new Process{StartInfo=new ProcessStartInfo(Engine.Tool("ffmpeg"),"-v error -nostdin -threads 2 -ss "+Engine.N(time)+" -i "+Engine.Q(path)+" -frames:v 1 -vf "+Engine.Q("scale=160:90:force_original_aspect_ratio=decrease,pad=160:90:(ow-iw)/2:(oh-ih)/2")+" -f image2pipe -c:v png pipe:1"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}}){
    p.Start();using(token.Register(()=>{try{if(!p.HasExited)p.Kill();}catch{}})){var error=p.StandardError.ReadToEndAsync();using(var buffer=new MemoryStream()){p.StandardOutput.BaseStream.CopyTo(buffer);p.WaitForExit();token.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new Exception("장면을 읽지 못했습니다.");return buffer.ToArray();}}
   }
  },token);}
  public static BitmapImage Decode(byte[] bytes){var b=new BitmapImage();using(var stream=new MemoryStream(bytes)){b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.StreamSource=stream;b.EndInit();}b.Freeze();return b;}
  public static async Task Render(string source,string output,VideoInfo info,Settings settings,IList<Segment> clips,CancellationToken token,Action<double> progress){
   string script=output+".filter";var preview=new Settings{Ratio=settings.Ratio,Mode=settings.Mode,Resolution="720p",Encoder="CPU",Fps=Math.Min(60,settings.Fps),X=settings.X,Y=settings.Y,Zoom=settings.Zoom,OffsetX=settings.OffsetX,OffsetY=settings.OffsetY};
   try{
    File.WriteAllText(script,Engine.Filter(info,preview,clips),new UTF8Encoding(false));
    string args=Engine.Arguments(source,output,script,info,preview,clips).Replace("-preset veryfast -crf 20","-preset ultrafast -crf 25");
    await Task.Run(()=>{
     token.ThrowIfCancellationRequested();using(var p=new Process{StartInfo=new ProcessStartInfo(Engine.Tool("ffmpeg"),args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}}){
      p.Start();using(token.Register(()=>{try{if(!p.HasExited)p.Kill();}catch{}})){var errors=p.StandardError.ReadToEndAsync();string line;double total=Arrangement.Length(clips);while((line=p.StandardOutput.ReadLine())!=null){double us;if(line.StartsWith("out_time_us=")&&double.TryParse(line.Substring(12),System.Globalization.NumberStyles.Float,Engine.Inv,out us)&&progress!=null)progress(Math.Min(99,us/1000000/total*100));}p.WaitForExit();token.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new Exception("편집 결과 미리보기를 만들지 못했습니다.\n"+string.Join("\n",errors.Result.Split('\n').Reverse().Take(5).Reverse()));}
     }
    },token);
   }catch{try{if(File.Exists(output))File.Delete(output);}catch{}throw;}finally{try{if(File.Exists(script))File.Delete(script);}catch{}}
  }
 }
 public partial class Editor {
  public bool Automated;
  MediaElement resultPlayer;bool resultMode,resultPlaying,previewBusy;
  CancellationTokenSource previewCancel,filmCancel;
  DispatcherTimer filmTimer;int filmGeneration;
  string projectPath,savedKey,resultCachePath,resultCacheKey;
  Dictionary<string,BitmapImage> filmCache=new Dictionary<string,BitmapImage>();
  Task filmTask=Task.FromResult(0);
  void InitializeFeatures(){
   resultPlayer=new MediaElement{LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Close,Stretch=Stretch.Fill,Visibility=Visibility.Collapsed,Volume=.8};C<Canvas>("Viewport").Children.Add(resultPlayer);
   resultPlayer.MediaOpened+=delegate{if(resultMode&&resultPlayer.NaturalDuration.HasTimeSpan)C<Slider>("ResultSeek").Maximum=resultPlayer.NaturalDuration.TimeSpan.TotalSeconds;};
   resultPlayer.MediaEnded+=delegate{resultPlaying=false;resultPlayer.Pause();C<Slider>("ResultSeek").Value=C<Slider>("ResultSeek").Maximum;C<Button>("Play").Content="▶";};
   resultPlayer.MediaFailed+=delegate{resultPlaying=false;C<Button>("Play").Content="▶";Status("편집 결과 재생에 실패했습니다. 원본 편집으로 돌아가 다시 시도하세요.");};
   C<Slider>("ResultSeek").ValueChanged+=delegate{if(!changing&&resultMode)resultPlayer.Position=TimeSpan.FromSeconds(C<Slider>("ResultSeek").Value);if(resultMode)UpdateTime();};
   Bind("ResultPreview",async delegate{if(previewBusy){previewCancel.Cancel();return;}if(resultMode){LeaveResult();return;}await ShowResult();});
   Bind("ProjectSave",delegate{SaveProjectDialog();});Bind("ProjectOpen",async delegate{await OpenProjectDialog();});
   filmTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};filmTimer.Tick+=delegate{filmTimer.Stop();filmTask=BuildFilmstrip();};
   timeline.ViewChanged+=ScheduleFilmstrip;timeline.SizeChanged+=delegate{ScheduleFilmstrip();};
  }
  ProjectData CaptureProject(){return new ProjectData{SourcePath=source,Settings=Options(),FpsText=C<ComboBox>("Fps").Text,Clips=clips.Select(c=>c.Copy()).ToList(),Position=seek.Value,Selection=list.SelectedIndex,ViewStart=timeline.ViewStart,ViewSpan=timeline.ViewSpan};}
  string StateKey(){if(info==null)return "";return new JavaScriptSerializer().Serialize(new{Source=source,Clips=clips.Select(c=>c.Copy()).ToArray(),Ratio=C<ComboBox>("Ratio").Text,Mode=C<ComboBox>("Fit").SelectedIndex,Resolution=C<ComboBox>("Resolution").Text,Fps=C<ComboBox>("Fps").Text,Encoder=C<ComboBox>("Encoder").SelectedIndex,X=C<Slider>("CropX").Value,Y=C<Slider>("CropY").Value,Zoom=C<Slider>("FrameZoom").Value,OffsetX=frameOffsetX,OffsetY=frameOffsetY});}
  bool HasChanges {get{return info!=null&&savedKey!=null&&savedKey!=StateKey();}}
  bool ConfirmLeave(){if(Automated||!HasChanges)return true;var answer=MessageBox.Show(this,"편집한 작업을 저장할까요?\n저장하면 자른 구간과 설정을 나중에 다시 불러올 수 있습니다.","작업 저장",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);return answer==MessageBoxResult.No||(answer==MessageBoxResult.Yes&&SaveProjectDialog());}
  bool SaveProjectDialog(){if(info==null||busy||loading||previewBusy)return false;var d=new SaveFileDialog{Title="편집 작업 저장",Filter="ClipDock 작업|*.clipdock",DefaultExt=".clipdock",AddExtension=true,FileName=projectPath??Path.Combine(Path.GetDirectoryName(source),Path.GetFileNameWithoutExtension(source)+".clipdock"),OverwritePrompt=true};if(d.ShowDialog(this)!=true)return false;try{Projects.Write(d.FileName,CaptureProject());projectPath=d.FileName;savedKey=StateKey();Refresh();Status("작업 저장 완료 · "+projectPath);return true;}catch(Exception e){Error(e);return false;}}
  async Task OpenProjectDialog(){if(busy||loading||previewBusy)return;var d=new OpenFileDialog{Title="편집 작업 열기",Filter="ClipDock 작업|*.clipdock"};if(d.ShowDialog(this)==true&&ConfirmLeave())await LoadProject(d.FileName,null);}
  async Task LoadProject(string path,string replacement){
   if(busy||loading||previewBusy)return;
   try{
    var p=Projects.Read(path);string resolved=replacement??Projects.ResolveSource(p,path);
    if(resolved==null){if(Automated)throw new Exception("원본 영상을 찾지 못했습니다.");var d=new OpenFileDialog{Title="원본 영상 위치가 바뀌었습니다. 원본을 다시 선택하세요",Filter="영상 파일|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.ts|모든 파일|*.*"};if(d.ShowDialog(this)!=true)return;resolved=d.FileName;}
    loading=true;Refresh();var data=await Task.Run(()=>Engine.Probe(resolved));Projects.Validate(p,data.Duration);if(closed)return;loading=false;
    await Load(resolved);if(source!=resolved||info==null)return;
    syncingSelection=true;clips.Clear();foreach(var c in p.Clips)clips.Add(c.Copy());list.SelectedIndex=Math.Max(-1,Math.Min(clips.Count-1,p.Selection));syncingSelection=false;
    C<ComboBox>("Ratio").SelectedItem=p.Settings.Ratio;C<ComboBox>("Fit").SelectedIndex=p.Settings.Mode=="늘림"?2:p.Settings.Mode=="채우기"?1:0;C<Slider>("FrameZoom").Value=p.Settings.Zoom;frameOffsetX=p.Settings.OffsetX;frameOffsetY=p.Settings.OffsetY;C<ComboBox>("Resolution").SelectedItem=p.Settings.Resolution;C<ComboBox>("Fps").Text=p.FpsText;C<ComboBox>("Encoder").SelectedIndex=p.Settings.Encoder=="NVIDIA"?1:0;C<Slider>("CropX").Value=p.Settings.X;C<Slider>("CropY").Value=p.Settings.Y;
    timeline.ViewSpan=p.ViewSpan;timeline.ViewStart=Math.Min(p.ViewStart,Math.Max(0,info.Duration-timeline.Span));seek.Value=Math.Min(info.Duration,p.Position);undo.Clear();redo.Clear();projectPath=path;SyncSelection();SyncScroll();savedKey=StateKey();Refresh();ScheduleFilmstrip();Status("작업을 불러왔습니다 · "+Path.GetFileName(path));
   }catch(Exception e){if(Automated)throw;Error(e);}finally{loading=false;if(!closed)Refresh();}
  }
  async Task OpenPath(string path){if(busy||loading||previewBusy||!ConfirmLeave())return;LeaveResult();if(string.Equals(Path.GetExtension(path),".clipdock",StringComparison.OrdinalIgnoreCase))await LoadProject(path,null);else await Load(path);}
  void ResetFeatureSource(){ResetFraming();ResetTrackScale();LeaveResult();DeleteResultCache();projectPath=null;filmGeneration++;if(filmCancel!=null)filmCancel.Cancel();filmCache.Clear();timeline.Frames.Clear();timeline.Drawing=false;savedKey=StateKey();ScheduleFilmstrip();}
  void RefreshFeatures(){
   if(resultPlayer==null)return;bool usable=info!=null&&!busy&&!loading;
   C<Button>("ProjectSave").IsEnabled=usable&&!previewBusy;C<Button>("ProjectOpen").IsEnabled=!busy&&!loading&&!previewBusy;
   C<Button>("ResultPreview").IsEnabled=usable&&(clips.Count>0||previewBusy||resultMode);C<Button>("ResultPreview").Content=previewBusy?"준비 취소":resultMode?"← 원본 편집":"▶ 편집 결과";
   C<Button>("Play").IsEnabled=usable&&!previewBusy;C<Button>("Save").IsEnabled=usable&&!previewBusy&&clips.Count>0;
   if(info!=null)Title="ClipDock · "+(projectPath==null?Path.GetFileName(source):Path.GetFileName(projectPath))+(HasChanges?" *":"");
  }
  void DeleteResultCache(){resultPlayer.Close();if(resultCachePath!=null)try{File.Delete(resultCachePath);}catch{}resultCachePath=null;resultCacheKey=null;}
  void LeaveResult(){if(resultPlayer==null)return;resultPlayer.Pause();resultPlaying=false;resultMode=false;resultPlayer.Visibility=Visibility.Collapsed;player.Visibility=Visibility.Visible;still.Visibility=Visibility.Visible;C<Slider>("ResultSeek").Visibility=Visibility.Collapsed;C<Button>("Play").Content="▶ 재생";Refresh();ScheduleStill();}
  async Task ShowResult(){
   if(info==null||busy||loading||clips.Count==0)return;if(playing)Toggle();string key=StateKey();previewBusy=true;previewCancel=new CancellationTokenSource();var cancellation=previewCancel;Refresh();
   try{
    if(resultCacheKey!=key||!File.Exists(resultCachePath)){DeleteResultCache();string path=Path.Combine(Path.GetTempPath(),"clipdock-preview-"+Guid.NewGuid().ToString("N")+".mp4");resultCachePath=path;
     await MediaJobs.Render(source,path,info,Options(),clips.Select(c=>c.Copy()).ToList(),cancellation.Token,p=>{if(!closed)Dispatcher.BeginInvoke(new Action(()=>{if(previewBusy)Status("편집 결과 준비 중 · "+p.ToString("0")+"%");}));});resultCacheKey=key;
    }
    if(closed||cancellation.IsCancellationRequested)return;
    resultMode=true;player.Pause();player.Visibility=Visibility.Hidden;still.Visibility=Visibility.Hidden;resultPlayer.Visibility=Visibility.Visible;resultPlayer.Source=new Uri(resultCachePath);resultPlayer.Position=TimeSpan.Zero;C<Slider>("ResultSeek").Maximum=Arrangement.Length(clips);C<Slider>("ResultSeek").Value=0;C<Slider>("ResultSeek").Visibility=Visibility.Visible;resultPlayer.Play();resultPlaying=true;C<Button>("Play").Content="❚❚";Preview();Status("편집 결과 · 저장 순서대로 재생합니다. 미리보기는 최대 60fps이며 저장 FPS는 유지됩니다.");
   }catch(OperationCanceledException){if(!closed)Status("편집 결과 준비를 취소했습니다.");DeleteResultCache();}catch(Exception e){DeleteResultCache();if(Automated)throw;if(!closed)Error(e);}finally{previewBusy=false;cancellation.Dispose();if(ReferenceEquals(previewCancel,cancellation))previewCancel=null;if(!closed)Refresh();}
  }
  void ToggleResult(){if(resultPlaying){resultPlayer.Pause();resultPlaying=false;}else{if(resultPlayer.Position.TotalSeconds>=C<Slider>("ResultSeek").Maximum-.01){resultPlayer.Position=TimeSpan.Zero;C<Slider>("ResultSeek").Value=0;}resultPlayer.Play();resultPlaying=true;}C<Button>("Play").Content=resultPlaying?"❚❚":"▶";}
  void TickResult(){if(!resultMode||!resultPlaying)return;changing=true;C<Slider>("ResultSeek").Value=resultPlayer.Position.TotalSeconds;changing=false;UpdateTime();}
  void ScheduleFilmstrip(){if(filmTimer==null||info==null||closed)return;filmGeneration++;if(filmCancel!=null)filmCancel.Cancel();filmTimer.Stop();filmTimer.Start();}
  async Task BuildFilmstrip(){
   if(info==null||closed)return;int version=filmGeneration;string input=source;double start=timeline.ViewStart,span=timeline.Span,duration=info.Duration,fps=info.Fps;int count=Math.Max(4,Math.Min(16,(int)Math.Ceiling(Math.Max(400,timeline.ActualWidth)/110)));
   if(filmCancel!=null)filmCancel.Cancel();var cancellation=new CancellationTokenSource();filmCancel=cancellation;timeline.FramesLoading=true;timeline.InvalidateVisual();
   try{var frames=new List<ThumbnailFrame>();for(int i=0;i<count;i++){
     cancellation.Token.ThrowIfCancellationRequested();double a=start+span*i/count,b=Math.Min(duration,start+span*(i+1)/count),time=Math.Max(0,Math.Min(duration-1/fps,(a+b)/2));string key=Engine.N(time);BitmapImage image;
     if(!filmCache.TryGetValue(key,out image)){image=MediaJobs.Decode(await MediaJobs.Frame(input,time,cancellation.Token));if(closed||version!=filmGeneration)return;if(filmCache.Count>=96)filmCache.Remove(filmCache.Keys.First());filmCache[key]=image;}
     frames.Add(new ThumbnailFrame{Start=a,End=b,Image=image});if(version==filmGeneration){timeline.Frames=new List<ThumbnailFrame>(frames);timeline.InvalidateVisual();RefreshEditView();}
    }
   }catch(OperationCanceledException){}catch{if(version==filmGeneration)timeline.FramesLoading=false;}finally{cancellation.Dispose();if(ReferenceEquals(filmCancel,cancellation))filmCancel=null;if(version==filmGeneration){timeline.FramesLoading=false;timeline.InvalidateVisual();}}
  }
  void StopFeatures(){filmTimer.Stop();filmGeneration++;if(filmCancel!=null)filmCancel.Cancel();if(previewCancel!=null)previewCancel.Cancel();resultPlayer.Close();DeleteResultCache();}
  public async Task TestFeatures(string input,string report){
   var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);passed.Add("PASS: "+name);};
   await Load(input);UpdateLayout();SetRange(0,.25,1.25);AddOriginal();SetRange(1,2,2.75);Move(-1);
   C<ComboBox>("Ratio").SelectedItem="9:16";C<ComboBox>("Fit").SelectedIndex=1;C<ComboBox>("Fps").Text="240";C<Slider>("CropX").Value=.2;C<Slider>("CropY").Value=.7;seek.Value=2.1;Refresh();
   string expected=StateKey(),project=report+".작업.clipdock";Projects.Write(project,CaptureProject());
   var loaded=Projects.Read(project);check(!Path.IsPathRooted(loaded.SourcePath)&&Projects.ResolveSource(loaded,project)==input,"Project uses portable relative source path with absolute fallback");
   DeleteClip();C<ComboBox>("Ratio").SelectedItem="1:1";await LoadProject(project,null);
   check(StateKey()==expected&&clips[0].Start==2&&clips[1].Start==.25,"Project restores reordered clips, crop, aspect and 240fps settings");
   check(!HasChanges&&Math.Abs(seek.Value-2.1)<.00001,"Project restore keeps position and starts clean");
   var missing=CaptureProject();missing.SourcePath=Path.Combine(Path.GetDirectoryName(report),"missing-video.mp4");string missingFile=report+".missing.clipdock";Projects.Write(missingFile,missing);
   check(Projects.ResolveSource(Projects.Read(missingFile),missingFile)==null,"Missing source is detected");await LoadProject(missingFile,input);check(StateKey()==expected,"Explicit relink restores project against selected source");
   var bad=CaptureProject();bad.Clips[0].End=999;string badFile=report+".invalid.clipdock";Projects.Write(badFile,bad);bool rejected=false;try{await LoadProject(badFile,null);}catch{rejected=true;}check(rejected&&StateKey()==expected,"Invalid project does not replace current edit");
   filmTimer.Stop();filmGeneration++;await BuildFilmstrip();check(timeline.Frames.Count>=4&&timeline.Frames.All(f=>f.Image!=null&&f.Image.IsFrozen),"Timeline loads actual decoded scene thumbnails");
   timeline.Zoom(2,1.5);filmTimer.Stop();await BuildFilmstrip();check(timeline.Frames.Count>=4&&timeline.Frames[0].Start>=timeline.ViewStart-.0001&&timeline.Frames.Last().End<=timeline.ViewStart+timeline.Span+.0001,"Zoom regenerates thumbnails for visible source range");timeline.ResetView();filmTimer.Stop();await BuildFilmstrip();
   await ShowResult();var preview=await Task.Run(()=>Engine.Probe(resultCachePath));
   check(resultMode&&!timeline.IsEnabled&&C<Button>("Save").IsEnabled,"Result playback isolates editing but allows export");
   check(Math.Abs(preview.Duration-1.75)<.04&&Math.Abs(preview.Fps-60)<.001&&preview.Width==720&&preview.Height==1280&&preview.Audio,"Result proxy respects order duration, vertical crop and audio at 60fps");
   check(C<ComboBox>("Fps").Text=="240","Preview does not change export FPS");
   for(int n=0;n<30&&!resultPlayer.NaturalDuration.HasTimeSpan;n++)await Task.Delay(100);
   check(resultPlayer.NaturalDuration.HasTimeSpan,"Windows media player opens the generated result");
   if(resultPlaying)ToggleResult();Shortcut(System.Windows.Input.Key.Space,System.Windows.Input.ModifierKeys.None,false);check(resultPlaying,"Space resumes edited-result playback");
   string cache=resultCachePath;DateTime written=File.GetLastWriteTimeUtc(cache);File.Copy(cache,report+".preview.mp4",true);LeaveResult();await ShowResult();
   check(resultCachePath==cache&&File.GetLastWriteTimeUtc(cache)==written,"Unchanged result is reused without re-encoding");LeaveResult();
   string cancelledFile=report+".cancelled.mp4";using(var token=new CancellationTokenSource()){token.Cancel();bool cancelled=false;try{await MediaJobs.Render(source,cancelledFile,info,Options(),clips.ToList(),token.Token,null);}catch(OperationCanceledException){cancelled=true;}check(cancelled&&!File.Exists(cancelledFile)&&!File.Exists(cancelledFile+".filter"),"Cancelled preview removes incomplete artifacts");}
   Remember();SetRange(0,2,2.5);check(HasChanges,"Editing a loaded project marks unsaved changes");await ShowResult();check(resultCachePath!=cache,"Edited result invalidates previous preview");LeaveResult();Restore(false);
   filmTimer.Stop();await BuildFilmstrip();SelectClip(0,false);seek.Value=2.1;Refresh();Status("작업 복원 · 이어보기 · 장면 썸네일 검증 완료");
   File.WriteAllText(report,string.Join(Environment.NewLine,passed),Encoding.UTF8);
  }
 }
}
