using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using Microsoft.Win32;

namespace ClipDock {
 public class Segment {
  public double Start {get;set;} public double End {get;set;}
  public List<Segment> Parts {get;set;} public double? Position {get;set;} [ScriptIgnore] public bool IsGap;
  [ScriptIgnore] public bool IsGroup {get{return Parts!=null&&Parts.Count>0;}}
  [ScriptIgnore] public double Duration {get{return IsGroup?Arrangement.Length(Parts):End-Start;}}
  [ScriptIgnore] public string Label {get {return IsGroup?"연결한 "+Parts.Count+"개 구간\n합계 "+Duration.ToString("0.###",CultureInfo.InvariantCulture)+"초":Clock(Start)+"  →  "+Clock(End)+"     ·     "+Duration.ToString("0.###",CultureInfo.InvariantCulture)+"초";}}
  public Segment Copy(){return new Segment{Start=Start,End=End,Position=Position,IsGap=IsGap,Parts=Parts==null?null:Parts.Select(c=>c.Copy()).ToList()};}
  public static List<Segment> Flatten(IEnumerable<Segment> clips){return clips.SelectMany(c=>c.IsGroup?c.Parts:new List<Segment>{c}).ToList();}
  public static string Clock(double n) {return TimeSpan.FromSeconds(Math.Max(0,n)).ToString(@"hh\:mm\:ss\.fff");}
 }
 public class VideoInfo {public int Width,Height; public double Duration,Fps; public bool Audio;}
 public class Settings {public string Ratio="원본",Mode="여백",Resolution="원본 크기 기준",Encoder="CPU"; public double Fps=60,X=.5,Y=.5,Zoom=1,OffsetX,OffsetY;}
 public static partial class Engine {
  public static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
  public static string Base=AppDomain.CurrentDomain.BaseDirectory;
  public static string Tool(string n) {string p=Path.Combine(Base,"tools",n+".exe"); if(!File.Exists(p)) throw new Exception("tools 폴더에 "+n+".exe가 없습니다."); return p;}
  public static string Q(string s) {return "\""+s.Replace("\"","\\\"")+"\"";}
  public static string N(double d) {return d.ToString("0.#########",Inv);}
  public static double Number(object o) {double n; return double.TryParse(Convert.ToString(o,Inv),NumberStyles.Float,Inv,out n)?n:0;}
  public static VideoInfo Probe(string path) {
   var p=new Process {StartInfo=new ProcessStartInfo(Tool("ffprobe"),"-v error -show_streams -show_format -of json "+Q(path)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8}};
   p.Start(); var err=p.StandardError.ReadToEndAsync(); string json=p.StandardOutput.ReadToEnd(); p.WaitForExit(); if(p.ExitCode!=0) throw new Exception("영상을 읽을 수 없습니다.\n"+err.Result);
   var root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
   var streams=((System.Collections.IEnumerable)root["streams"]).Cast<Dictionary<string,object>>().ToArray();
   var v=streams.FirstOrDefault(x=>Convert.ToString(x["codec_type"])=="video" && (!x.ContainsKey("disposition") || Number(((Dictionary<string,object>)x["disposition"])["attached_pic"])==0));
   if(v==null) throw new Exception("영상 트랙이 없습니다.");
   string rate=Convert.ToString(v.ContainsKey("avg_frame_rate")?v["avg_frame_rate"]:"0/1"); var parts=rate.Split('/'); double fps=parts.Length==2 && Number(parts[1])>0?Number(parts[0])/Number(parts[1]):Number(rate);
   double duration=v.ContainsKey("duration")?Number(v["duration"]):0;
   if(duration<=0 && root.ContainsKey("format")) {var f=(Dictionary<string,object>)root["format"]; if(f.ContainsKey("duration")) duration=Number(f["duration"]);}
   int w=(int)Number(v["width"]),h=(int)Number(v["height"]);
   if(v.ContainsKey("side_data_list")) foreach(var item in (System.Collections.IEnumerable)v["side_data_list"]) {var d=item as Dictionary<string,object>; if(d!=null && d.ContainsKey("rotation") && Math.Abs(Number(d["rotation"]))%180>1) {int t=w;w=h;h=t;break;}}
   if(duration<=0 || w<=0 || h<=0) throw new Exception("영상의 길이나 크기를 확인할 수 없습니다.");
   return new VideoInfo {Width=w,Height=h,Duration=duration,Fps=fps>0?fps:60,Audio=streams.Any(x=>Convert.ToString(x["codec_type"])=="audio")};
  }
  public static int[] Size(VideoInfo info,Settings s) {
   double aspect=(double)info.Width/info.Height;
   if(s.Ratio=="16:9") aspect=16.0/9; if(s.Ratio=="9:16") aspect=9.0/16; if(s.Ratio=="4:3") aspect=4.0/3; if(s.Ratio=="1:1") aspect=1;
   double edge=Math.Max(info.Width,info.Height);
   if(s.Resolution=="720p") edge=1280; if(s.Resolution=="1080p") edge=1920; if(s.Resolution=="1440p") edge=2560;
   return new[]{Math.Max(2,(int)Math.Round((aspect>=1?edge:edge*aspect)/2)*2),Math.Max(2,(int)Math.Round((aspect>=1?edge/aspect:edge)/2)*2)};
  }
  public static string Filter(VideoInfo info,Settings s,IList<Segment> clips) {
   clips=Arrangement.Plan(clips);
   if(double.IsNaN(s.Fps)||double.IsInfinity(s.Fps)||s.Fps<1||s.Fps>1000)throw new Exception("FPS는 1~1000 사이여야 합니다.");
   if(clips.Count==0 || clips.Any(c=>double.IsNaN(c.Start) || double.IsNaN(c.End) || double.IsInfinity(c.Start) || double.IsInfinity(c.End) || c.Start<0 || c.End<=c.Start || (!c.IsGap&&c.End>info.Duration+.01))) throw new Exception("구간의 시작/끝 시간을 확인하세요.");
   var b=new StringBuilder(); int count=clips.Count;
   for(int i=0;i<count;i++) {
    string duration=N(clips[i].End-clips[i].Start);
    if(clips[i].IsGap){b.Append("color=c=black:s="+info.Width+"x"+info.Height+":r="+N(s.Fps)+":d="+duration+",setsar=1[v"+i+"];");if(info.Audio)b.Append("anullsrc=r=48000:cl=stereo,atrim=duration="+duration+"[a"+i+"];");continue;}
    b.Append("["+i+":v:0]trim=duration="+duration+",setpts=PTS-STARTPTS[v"+i+"];");
    if(info.Audio) b.Append("["+i+":a:0]atrim=duration="+duration+",asetpts=PTS-STARTPTS,aresample=48000,apad,atrim=duration="+duration+"[a"+i+"];");
   }
   for(int i=0;i<count;i++) {b.Append("[v"+i+"]");if(info.Audio)b.Append("[a"+i+"]");}
   b.Append("concat=n="+count+":v=1:a="+(info.Audio?"1[cv][outa];":"0[cv];"));
   int[] z=Size(info,s); string w=z[0].ToString(),h=z[1].ToString();
   var place=Placement(info,s);
   b.Append("[cv]fps="+N(s.Fps)+",scale="+place.Width+":"+place.Height+",setsar=1[scaled];");
   b.Append("color=c=black:s="+w+"x"+h+":r="+N(s.Fps)+":d="+N(clips.Sum(c=>c.Duration))+"[canvas];");
   b.Append("[canvas][scaled]overlay=x="+N(place.Left)+":y="+N(place.Top)+":shortest=1,setsar=1,format=yuv420p[outv]"); return b.ToString();
  }
  public static string Arguments(string input,string output,string filter,VideoInfo info,Settings s,IList<Segment> clips) {
   clips=Arrangement.Plan(clips);
   string codec=s.Encoder=="NVIDIA"?"-c:v h264_nvenc -preset p4 -rc vbr -cq 20 -b:v 0":"-c:v libx264 -preset veryfast -crf 20";
   string inputs=string.Join(" ",clips.Select(c=>"-threads 2 -ss "+N(c.Start)+" -t "+N(c.End-c.Start)+" -i "+Q(input)));
   return "-hide_banner -nostdin -y -filter_complex_threads 2 "+inputs+" -/filter_complex "+Q(filter)+" -map "+Q("[outv]")+(info.Audio?" -map "+Q("[outa]")+" -c:a aac -b:a 192k":" -an")+" "+codec+" -pix_fmt yuv420p -fps_mode cfr -movflags +faststart -progress pipe:1 -nostats "+Q(output);
  }
 }
 public partial class Editor : Window {
  const string Layout=@"
<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='#10131B'>
 <Grid.Resources>
  <Style TargetType='TextBlock'><Setter Property='Foreground' Value='#E6EAF2'/><Setter Property='VerticalAlignment' Value='Center'/></Style>
  <Style TargetType='Button'><Setter Property='Background' Value='#293245'/><Setter Property='Foreground' Value='#F0F4FF'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='14,9'/><Setter Property='Margin' Value='3'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='7' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Opacity' Value='.8'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='bg' Property='Opacity' Value='.35'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType='TextBox'><Setter Property='Background' Value='#101722'/><Setter Property='Foreground' Value='#F0F4FF'/><Setter Property='BorderBrush' Value='#354056'/><Setter Property='Padding' Value='9,7'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='SelectionBrush' Value='#527DC9'/></Style>
  <Style TargetType='ComboBoxItem'><Setter Property='Foreground' Value='#E6EAF2'/><Setter Property='Background' Value='#202838'/><Setter Property='Padding' Value='12,9'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border x:Name='item' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' CornerRadius='4'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='item' Property='Background' Value='#354F74'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='item' Property='Background' Value='#2D4566'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType='ComboBox'>
   <Setter Property='Margin' Value='0,7,0,15'/><Setter Property='MinHeight' Value='40'/><Setter Property='Foreground' Value='#EDF3FF'/><Setter Property='Background' Value='#101722'/><Setter Property='BorderBrush' Value='#3A465D'/><Setter Property='HorizontalContentAlignment' Value='Left'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
    <Grid>
     <Border x:Name='outline' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='7'/>
     <ToggleButton x:Name='DropToggle' Focusable='False' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' ClickMode='Press' Background='Transparent' BorderThickness='0'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='Transparent'><TextBlock Text='⌄' Foreground='#AABBD4' FontSize='17' HorizontalAlignment='Right' Margin='0,0,13,4'/></Border></ControlTemplate></ToggleButton.Template></ToggleButton>
     <ContentPresenter x:Name='SelectedContent' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Margin='12,0,38,0' VerticalAlignment='Center' IsHitTestVisible='False'/>
     <TextBox x:Name='PART_EditableTextBox' Visibility='Hidden' Background='Transparent' Foreground='#EDF3FF' BorderThickness='0' Margin='4,0,36,0' VerticalContentAlignment='Center' IsReadOnly='{TemplateBinding IsReadOnly}'/>
     <Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
      <Border Background='#202838' BorderBrush='#465776' BorderThickness='1' CornerRadius='7' Padding='4' Margin='0,5,0,0' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='280'><ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border>
     </Popup>
    </Grid>
    <ControlTemplate.Triggers><Trigger Property='IsEditable' Value='True'><Setter TargetName='PART_EditableTextBox' Property='Visibility' Value='Visible'/><Setter TargetName='SelectedContent' Property='Visibility' Value='Hidden'/><Setter TargetName='DropToggle' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='DropToggle' Property='Width' Value='34'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='outline' Property='BorderBrush' Value='#6888B6'/></Trigger><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='outline' Property='BorderBrush' Value='#78AFFF'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger></ControlTemplate.Triggers>
   </ControlTemplate></Setter.Value></Setter>
  </Style>  <Style TargetType='ListBox'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBox'><Border Background='#101722' CornerRadius='5'><ScrollViewer Focusable='False' Padding='3'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType='ListBoxItem'><Setter Property='Foreground' Value='#EDF2FF'/><Setter Property='Padding' Value='12,9'/><Setter Property='Margin' Value='2'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='item' Background='#202939' CornerRadius='5' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='item' Property='Background' Value='#345788'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 </Grid.Resources>
 <Grid.RowDefinitions><RowDefinition Height='76'/><RowDefinition Height='*'/><RowDefinition Height='46'/></Grid.RowDefinitions>
 <Border Grid.Row='0' BorderBrush='#293041' BorderThickness='0,0,0,1' Padding='23,10'><DockPanel><StackPanel DockPanel.Dock='Left'><TextBlock x:Name='Brand' Text='ClipDock' FontSize='26' FontWeight='Bold'/><TextBlock x:Name='Tagline' Text='게임의 좋은 순간만, 빠르게.' FontSize='12' Foreground='#99A8BF'/></StackPanel><StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'><Button x:Name='ProjectOpen' Content='작업 열기' ToolTip='Ctrl+Shift+O'/><Button x:Name='ProjectSave' Content='작업 저장' ToolTip='Ctrl+Shift+S'/><Button x:Name='Open' Content='영상 열기   Ctrl+O'/><Button x:Name='Save' Content='MP4 저장   Ctrl+S' Background='#3D78DE' Margin='14,3,3,3'/><Button x:Name='Cancel' Content='저장 취소' Visibility='Collapsed'/></StackPanel></DockPanel></Border>
 <Grid x:Name='EditorBody' Grid.Row='1' Margin='20,16,20,10'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
  <Grid x:Name='EditorRows'><Grid.RowDefinitions><RowDefinition Height='*' MinHeight='96'/><RowDefinition Height='66'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
   <Border Grid.Row='0' Background='#080B11' CornerRadius='10' BorderBrush='#293041' BorderThickness='1' ClipToBounds='True'><Grid x:Name='Stage' Margin='8'><Canvas x:Name='Viewport' Background='Black' ClipToBounds='True' HorizontalAlignment='Center' VerticalAlignment='Center'><MediaElement x:Name='Player' LoadedBehavior='Manual' UnloadedBehavior='Close' Stretch='Fill'/></Canvas><StackPanel x:Name='Empty' HorizontalAlignment='Center' VerticalAlignment='Center'><TextBlock Text='영상을 여기에 놓으세요' FontSize='23' HorizontalAlignment='Center'/><TextBlock Text='MP4 · MOV · MKV · AVI · WebM' Foreground='#8493AC' Margin='0,12,0,0' HorizontalAlignment='Center'/></StackPanel><Border Background='#C0101722' VerticalAlignment='Top' HorizontalAlignment='Left' CornerRadius='5' Padding='9,5' Margin='8'><TextBlock x:Name='PreviewLabel' Text='PREVIEW' FontSize='11' Foreground='#ADBBD2'/></Border><Border x:Name='FrameTools' Visibility='Collapsed' VerticalAlignment='Bottom' Margin='6' Padding='10,7' CornerRadius='8' Background='#F0182233' BorderBrush='#486387' BorderThickness='1'>
 <StackPanel>
  <WrapPanel VerticalAlignment='Center'><TextBlock Text='출력 화면' Margin='0,0,8,0'/><ComboBox x:Name='FrameCanvas' Width='170' Margin='0,0,12,0' MinHeight='32'/><Button x:Name='FrameFit' Content='전체 보이기' Padding='9,5'/><Button x:Name='FrameFill' Content='여백 없이 채우기' Padding='9,5'/><Button x:Name='FrameStretch' Content='화면에 늘림' Padding='9,5'/><Button x:Name='FrameReset' Content='초기화' Padding='9,5'/></WrapPanel>
  <WrapPanel Margin='0,3,0,0'><TextBlock Text='크기' Margin='0,0,8,0'/><Slider x:Name='FrameZoom' Minimum='.1' Maximum='5' Value='1' Width='160' SmallChange='.01' LargeChange='.1' VerticalAlignment='Center'/><TextBlock x:Name='FramePercent' Text='100%' Width='55' Margin='8,0'/><TextBlock Text='영상 드래그: 위치 이동 · 휠: 확대/축소 · Esc: 닫기' Foreground='#A9BEDC' FontSize='10'/></WrapPanel>
 </StackPanel>
</Border></Grid></Border>
   <Grid Grid.Row='1' Margin='0,5'><Grid.RowDefinitions><RowDefinition Height='36'/><RowDefinition Height='20'/></Grid.RowDefinitions><Slider x:Name='ResultSeek' Grid.Row='1' Visibility='Collapsed' IsMoveToPointEnabled='True' Minimum='0' Maximum='1'/><Slider x:Name='Seek' Minimum='0' Maximum='1' Visibility='Collapsed'/><DockPanel><Button x:Name='Play' Content='▶ 재생' Padding='17,5' ToolTip='재생 / 일시 정지 (Space)'/><Button x:Name='FrameToggle' Content='화면 조절' Padding='12,5'/><Button x:Name='ResultPreview' Visibility='Collapsed'/><TextBlock x:Name='Position' Text='00:00:00.000 / 00:00:00.000' FontFamily='Consolas' Margin='12,0'/><TextBlock x:Name='Meta' Text='영상 없음' Foreground='#8D9BB2' FontSize='11' HorizontalAlignment='Right'/></DockPanel></Grid>
   <Border Grid.Row='2' Background='#181E2B' CornerRadius='10' Padding='14,10'><StackPanel>
    <DockPanel Margin='0,0,0,8'><TextBlock Text='편집 타임라인' FontSize='15' FontWeight='Bold'/><TextBlock x:Name='Total' Foreground='#9DB3D0' HorizontalAlignment='Right'/></DockPanel>
    <DockPanel><StackPanel Orientation='Horizontal'><Button x:Name='Split' Content='분할   S' Background='#3D78DE' ToolTip='재생 막대 위치에서 두 조각으로 나눕니다.'/><Button x:Name='Remove' Content='삭제   Del' ToolTip='선택 조각만 지우고 빈자리는 남깁니다.'/><Button x:Name='Undo' Content='↶' ToolTip='실행 취소 · Ctrl+Z'/><Button x:Name='More' Content='···' ToolTip='합치기, 순서 변경, 다시 실행'/></StackPanel><TextBlock x:Name='TrackHint' Text='위 눈금: 탐색 · 조각 드래그: 이동 · 양끝: 자르기' FontSize='11' Foreground='#8FA2BF' HorizontalAlignment='Right'/></DockPanel>
    <DockPanel Margin='0,5,0,0'><StackPanel Orientation='Horizontal'><Button x:Name='TrackMinus' Content='−' Padding='10,4'/><Slider x:Name='TrackZoom' Width='130' Minimum='10' Maximum='400' Value='100' VerticalAlignment='Center' ToolTip='타임라인 확대 / 축소'/><Button x:Name='TrackPlus' Content='＋' Padding='10,4'/><Button x:Name='TrackFit' Content='전체 보기' Padding='9,4'/><Button x:Name='CloseGaps' Content='빈칸 없애기' Padding='9,4' ToolTip='원할 때만 모든 조각을 앞에서부터 붙입니다.'/></StackPanel></DockPanel>
    <ScrollViewer x:Name='EditScroll' HorizontalScrollBarVisibility='Auto' VerticalScrollBarVisibility='Disabled' Margin='0,7,0,0'><ContentControl x:Name='EditHost' Height='142'/></ScrollViewer>
    <TextBlock x:Name='BottomHint' Text='빈칸은 검정 화면 / 무음 · 조각을 끌어 위치 변경 · Ctrl+클릭: 여러 조각 선택' Foreground='#8FA2BF' FontSize='11' Margin='4,7,0,10'/>
    <Expander x:Name='SourcePanel' Visibility='Collapsed' Foreground='#AEC2DF'>
     <StackPanel Margin='0,10,0,0'>
      <DockPanel><Button x:Name='Add' Content='+ 구간 그리기  N' HorizontalAlignment='Left'/><StackPanel Orientation='Horizontal' HorizontalAlignment='Right'><Button x:Name='ZoomOut' Content='−' Padding='10,4'/><Button x:Name='ZoomIn' Content='＋' Padding='10,4'/><Button x:Name='ZoomAll' Content='전체 보기' Padding='9,4'/></StackPanel></DockPanel>
      <ContentControl x:Name='TimelineHost' Height='120'/>
      <ScrollBar x:Name='TimelineScroll' Orientation='Horizontal' Height='12' Minimum='0' Maximum='0' Margin='18,2,18,5'/>
      <Expander Header='시간 직접 입력' Foreground='#8FA2BF' Margin='3,8,0,0'><Grid Margin='0,6,0,0'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><TextBox x:Name='In' Text='00:00:00.000'/><TextBlock Grid.Column='1' Text=' → ' Margin='8,0'/><TextBox x:Name='Out' Grid.Column='2' Text='00:00:00.000'/><Button x:Name='Update' Grid.Column='3' Content='적용'/></Grid></Expander>
     </StackPanel>
    </Expander>
    <StackPanel Visibility='Collapsed'><Button x:Name='MarkIn'/><Button x:Name='MarkOut'/><Button x:Name='Merge'/><Button x:Name='Unmerge'/><Button x:Name='Redo'/><Button x:Name='Up'/><Button x:Name='Down'/><ListBox x:Name='Clips' SelectionMode='Extended'/></StackPanel>   </StackPanel></Border>
  </Grid>
 </Grid>
 <Border Grid.Row='2' BorderBrush='#293041' BorderThickness='0,1,0,0' Padding='23,7'><DockPanel><ProgressBar x:Name='Progress' DockPanel.Dock='Right' Width='160' Height='4' Minimum='0' Maximum='100' Background='#293245' Foreground='#639DFF' BorderThickness='0' Margin='16,0,0,0'/><TextBlock x:Name='Status' Text='영상을 열거나 창에 끌어다 놓으세요.' Foreground='#A0B0C7' FontSize='12' TextTrimming='CharacterEllipsis'/></DockPanel></Border>
 <Border x:Name='ExportOverlay' KeyboardNavigation.TabNavigation='Cycle' KeyboardNavigation.ControlTabNavigation='Cycle' Grid.RowSpan='3' Background='#D9080B12' Visibility='Collapsed' Padding='24'>
  <Border Width='480' MaxHeight='800' Background='#181E2B' CornerRadius='14' BorderBrush='#354157' BorderThickness='1' Padding='28' HorizontalAlignment='Center' VerticalAlignment='Center'>
   <Grid><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
    <StackPanel Margin='0,0,0,20'><TextBlock Text='MP4 저장' FontSize='23' FontWeight='Bold'/><TextBlock Text='화면과 화질을 고른 뒤 저장할 위치를 선택하세요.' Foreground='#92A4BE' Margin='0,7,0,0'/></StackPanel>
    <ContentControl Grid.Row='1'><ScrollViewer VerticalScrollBarVisibility='Auto'><StackPanel><TextBlock Text='화면 비율' FontSize='12' Foreground='#9DACBF'/><ComboBox x:Name='Ratio'/><TextBlock Text='화면 맞추기' FontSize='12' Foreground='#9DACBF'/><ComboBox x:Name='Fit'/><StackPanel x:Name='CropPanel' Visibility='Collapsed' Margin='0,0,0,12'><TextBlock Text='잘라낼 위치 · 가로 / 세로' FontSize='11' Foreground='#9DACBF'/><Slider x:Name='CropX' Minimum='0' Maximum='1' Value='.5' Margin='0,6'/><Slider x:Name='CropY' Minimum='0' Maximum='1' Value='.5'/></StackPanel><TextBlock Text='해상도 · 긴 변 기준' FontSize='12' Foreground='#9DACBF'/><ComboBox x:Name='Resolution'/><TextBlock Text='저장 FPS · 직접 입력 가능' FontSize='12' Foreground='#9DACBF'/><ComboBox x:Name='Fps' IsEditable='True' IsTextSearchEnabled='False' StaysOpenOnEdit='True'/><TextBlock x:Name='FpsHint' Text='원본 프레임을 유지합니다.' Foreground='#92A4BE' FontSize='11' TextWrapping='Wrap' Margin='0,-7,0,14'/><TextBlock Text='인코더' FontSize='12' Foreground='#9DACBF'/><ComboBox x:Name='Encoder'/><TextBlock Text='원본 게임 소리는 유지됩니다.' FontSize='11' Foreground='#7F90AC' Margin='0,0,0,8'/></StackPanel></ScrollViewer>    </ContentControl>
    <StackPanel Grid.Row='2' Margin='0,8,0,0'><TextBlock x:Name='Output' Foreground='#A7BBD9' FontSize='12' Margin='0,0,0,12'/><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='2*'/></Grid.ColumnDefinitions><Button x:Name='ExportBack' Content='편집으로 돌아가기' Margin='0,0,8,0'/><Button x:Name='ExportConfirm' Grid.Column='1' Content='저장 위치 선택 →' Background='#3D78DE' Margin='0'/></Grid></StackPanel>
   </Grid>
  </Border>
 </Border></Grid>";
  Grid root; MediaElement player; Image still; Slider seek; ListBox list; ObservableCollection<Segment> clips=new ObservableCollection<Segment>();
  Timeline timeline; bool syncingSelection, syncingScroll;
  class EditState {public List<Segment> Clips;public int Selection;public List<int> Selections;public FrameState Frame;}
  Stack<EditState> undo=new Stack<EditState>(),redo=new Stack<EditState>();
  DispatcherTimer stillTimer; Process thumbnail; int stillVersion;
  DispatcherTimer timer; VideoInfo info; string source; bool playing,busy,loading,closed,changing; Process encoding; string partial,filterFile; StringBuilder errorLog=new StringBuilder();
  T C<T>(string name) where T:FrameworkElement {return (T)root.FindName(name);}
  void Status(string s) {C<TextBlock>("Status").Text=s;}
  void Error(Exception e) {Status(e.Message.Split('\n')[0]);MessageBox.Show(this,e.Message,"ClipDock",MessageBoxButton.OK,MessageBoxImage.Warning);}
  public Editor() {
   Title="ClipDock · 게임 클립 편집기";Width=Math.Max(640,Math.Min(1240,SystemParameters.WorkArea.Width-48));Height=Math.Max(600,Math.Min(940,SystemParameters.WorkArea.Height-48));MinWidth=640;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=new SolidColorBrush(Color.FromRgb(16,19,27));FontFamily=new FontFamily("Malgun Gothic");FontSize=12;AllowDrop=true;
   root=(Grid)XamlReader.Parse(Layout);Content=root;player=C<MediaElement>("Player");still=new Image{Stretch=Stretch.Fill,IsHitTestVisible=false};C<Canvas>("Viewport").Children.Add(still);seek=C<Slider>("Seek");list=C<ListBox>("Clips");list.ItemsSource=clips;
   timeline=new Timeline{Clips=clips};C<ContentControl>("TimelineHost").Content=timeline;
   timeline.SelectRequested+=SelectFromTimeline;
   timeline.SeekRequested+=delegate(double t){if(playing)Toggle();seek.Value=t;};
   timeline.EditStarted+=delegate{if(playing)Toggle();Remember();};
   timeline.TrimRequested+=delegate(int i,double a,double b){SetRange(i,a,b);};
   timeline.EditFinished+=delegate{Status("구간 길이를 변경했습니다. · Ctrl+Z로 되돌릴 수 있어요.");Refresh();};
   timeline.ViewChanged+=SyncScroll;
   C<ScrollBar>("TimelineScroll").ValueChanged+=delegate{if(!syncingScroll){timeline.ViewStart=C<ScrollBar>("TimelineScroll").Value;timeline.InvalidateVisual();}};
   stillTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};stillTimer.Tick+=async delegate{stillTimer.Stop();await StillFrame();};
   Fill("Ratio",new[]{"원본","16:9","9:16","4:3","1:1"}); Fill("Fit",new[]{"여백 추가","꽉 채우기","화면에 늘림"});Fill("Resolution",new[]{"원본 크기 기준","720p","1080p","1440p"});Fill("Fps",new[]{"원본 유지","30","60","120","144","165","240"});Fill("Encoder",new[]{"CPU · 호환성 우선","NVIDIA · GPU 사용"});
   Bind("Open",delegate{OpenDialog();});Bind("Play",delegate{Toggle();});Bind("MarkIn",delegate{Mark(true);});Bind("MarkOut",delegate{Mark(false);});Bind("Update",delegate{ApplyTimes();});Bind("Remove",delegate{DeleteClip();});Bind("Up",delegate{Move(-1);});Bind("Down",delegate{Move(1);});Bind("Save",async delegate{await Export();});Bind("Cancel",delegate{Cancel();});
   Bind("Split",delegate{SplitClip();});Bind("Undo",delegate{Restore(false);});Bind("Redo",delegate{Restore(true);});
   Bind("ZoomIn",delegate{timeline.Zoom(2,seek.Value);});Bind("ZoomOut",delegate{timeline.Zoom(.5,seek.Value);});Bind("ZoomAll",delegate{timeline.ResetView();});
   foreach(string id in new[]{"Ratio","Fit","Resolution","Fps","Encoder"}) C<ComboBox>(id).SelectionChanged+=delegate{Dispatcher.BeginInvoke(new Action(Refresh));};
   C<ComboBox>("Fps").AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler(delegate{Dispatcher.BeginInvoke(new Action(Refresh));}));
   C<Slider>("CropX").ValueChanged+=delegate{Refresh();};C<Slider>("CropY").ValueChanged+=delegate{Refresh();};
   C<Grid>("Stage").SizeChanged+=delegate{Preview();};
   seek.ValueChanged+=delegate{if(!changing && info!=null){if(playing)PauseLive();liveCursorValid=false;gapPreview=false;player.Visibility=Visibility.Visible;player.IsMuted=false;player.Position=TimeSpan.FromSeconds(seek.Value);ScheduleStill();}UpdateTime();};
   list.SelectionChanged+=delegate{if(!syncingSelection){SyncSelection();var c=list.SelectedItem as Segment;if(c!=null&&list.SelectedItems.Count==1){selectionAnchor=list.SelectedIndex;if(playing)Toggle();seek.Value=Segment.Flatten(new[]{c}).First().Start;timeline.Reveal(seek.Value);}Refresh();}};
   player.MediaOpened+=delegate{Preview();ScheduleStill();};player.MediaEnded+=delegate{if(playing)return;playing=false;player.Pause();C<Button>("Play").Content="▶ 재생";ScheduleStill();};
   player.MediaFailed+=delegate{playing=false;C<Button>("Play").Content="▶ 재생";ScheduleStill();Status("이 코덱의 실시간 재생은 Windows에서 지원하지 않습니다. 탐색 시 정지 화면과 MP4 저장은 사용할 수 있어요.");};
   DragOver+=delegate(object sender,DragEventArgs e){e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)&&!busy&&!loading&&!previewBusy?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
   Drop+=async delegate(object sender,DragEventArgs e){if(C<Border>("ExportOverlay").Visibility!=Visibility.Visible&&!busy&&!loading&&!previewBusy&&e.Data.GetDataPresent(DataFormats.FileDrop)){string[] files=(string[])e.Data.GetData(DataFormats.FileDrop);if(files.Length>0)await OpenPath(files[0]);}};
   InitializeFeatures();InitializeGrouping();InitializeEditView();InitializeFraming();Bind("ExportBack",delegate{CloseExportSettings();});Bind("ExportConfirm",async delegate{await StartExport();});PreviewKeyDown+=Keys;SizeChanged+=delegate{AdaptEditorSize();};Loaded+=delegate{AdaptEditorSize();};
   timer=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};timer.Tick+=delegate{TickLive();};timer.Start();
   Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!busy&&!ConfirmLeave()){e.Cancel=true;return;}if(busy){if(MessageBox.Show(this,"진행 중인 저장을 취소하고 닫을까요?","ClipDock",MessageBoxButton.YesNo)!=MessageBoxResult.Yes){e.Cancel=true;return;}Cancel();try{if(encoding!=null)encoding.WaitForExit(3000);}catch{}foreach(string f in new[]{partial,filterFile})try{if(f!=null&&File.Exists(f))File.Delete(f);}catch{}}closed=true;StopFeatures();timer.Stop();stillTimer.Stop();try{if(thumbnail!=null&&!thumbnail.HasExited)thumbnail.Kill();}catch{}player.Close();};
   Refresh();
  }
  void Fill(string name,string[] items) {var box=C<ComboBox>(name);foreach(string s in items)box.Items.Add(s);box.SelectedIndex=0;}
  void Bind(string id,RoutedEventHandler action) {C<Button>(id).Click+=action;}
  async void OpenDialog() {if(busy||loading||previewBusy)return;var d=new OpenFileDialog {Title="편집할 영상 선택",Filter="영상 파일|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.ts|모든 파일|*.*"};if(d.ShowDialog(this)==true)await OpenPath(d.FileName);}
  async Task Load(string path) {
   if(playing)PauseLive();ResetLive();loading=true;Refresh();Status("영상 정보를 확인하는 중…");
   try {var data=await Task.Run(()=>Engine.Probe(path));if(closed)return;player.Close();playing=false;source=path;info=data;syncingSelection=true;clips.Clear();clips.Add(new Segment{Start=0,End=data.Duration});list.SelectedIndex=0;syncingSelection=false;undo.Clear();redo.Clear();timeline.Duration=data.Duration;timeline.Fps=data.Fps;timeline.ResetView();SyncSelection();still.Source=null;player.Source=new Uri(path);player.IsMuted=false;player.Volume=.8;player.Play();player.Pause();seek.Maximum=data.Duration;seek.Value=0;C<TextBox>("In").Text=Segment.Clock(0);C<TextBox>("Out").Text=Segment.Clock(data.Duration);C<StackPanel>("Empty").Visibility=Visibility.Collapsed;C<Button>("Play").Content="▶ 재생";C<TextBlock>("Meta").Text=data.Width+"×"+data.Height+" · "+data.Fps.ToString("0.###")+" fps";Title="ClipDock · "+Path.GetFileName(path);ResetFeatureSource();Status(Path.GetFileName(path)+" · 양끝 손잡이를 드래그하거나 S로 분할하세요.");}
   catch(Exception e){if(!closed)Error(e);}finally{loading=false;if(!closed){Refresh();ScheduleStill();}}
  }
  void Toggle(){ToggleLive();}
  void ScheduleStill(){if(info==null||playing||closed||resultMode||previewBusy)return;stillVersion++;stillTimer.Stop();stillTimer.Start();}
  async Task StillFrame(){if(gapPreview)return;
   if(info==null||playing||closed||resultMode||previewBusy)return;int version=stillVersion;string path=source;double position=Math.Min(seek.Value,Math.Max(0,info.Duration-1/info.Fps));
   try{if(thumbnail!=null&&!thumbnail.HasExited)thumbnail.Kill();}catch{}
   try{
    byte[] bytes=await Task.Run(()=>{
     var process=new Process{StartInfo=new ProcessStartInfo(Engine.Tool("ffmpeg"),"-v error -nostdin -threads 2 -ss "+Engine.N(position)+" -i "+Engine.Q(path)+" -frames:v 1 -vf "+Engine.Q("scale=1280:720:force_original_aspect_ratio=decrease")+" -f image2pipe -c:v png pipe:1"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
     thumbnail=process;try{process.Start();var errors=process.StandardError.ReadToEndAsync();using(var memory=new MemoryStream()){process.StandardOutput.BaseStream.CopyTo(memory);process.WaitForExit();if(process.ExitCode!=0)return null;return memory.ToArray();}}finally{if(object.ReferenceEquals(thumbnail,process))thumbnail=null;process.Dispose();}
    });
    if(gapPreview||closed||playing||version!=stillVersion||bytes==null||bytes.Length==0)return;
    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=new MemoryStream(bytes);image.EndInit();image.Freeze();still.Source=image;still.Visibility=Visibility.Visible;Preview();
   }catch{}
  }
  bool Ready {get{return info!=null&&!busy&&!loading&&!previewBusy&&!resultMode;}}
  EditState Capture(){return new EditState{Clips=clips.Select(c=>c.Copy()).ToList(),Selection=list.SelectedIndex,Selections=SelectedIndices(),Frame=CaptureFrame()};}
  void Remember(){if(playing)PauseLive();undo.Push(Capture());redo.Clear();}
  void Restore(bool forward){if(!Ready)return;var from=forward?redo:undo;var to=forward?undo:redo;if(from.Count==0)return;to.Push(Capture());var state=from.Pop();RestoreFrame(state.Frame);gapPreview=false;player.Visibility=Visibility.Visible;syncingSelection=true;clips.Clear();foreach(var c in state.Clips)clips.Add(c.Copy());list.SelectedIndex=state.Selection;syncingSelection=false;SelectMany(state.Selections??new List<int>{state.Selection});Status(forward?"다시 실행했습니다.":"이전 편집으로 되돌렸습니다.");}
  void SyncSelection(){timeline.SelectedIndex=list.SelectedIndex;timeline.SelectedIndices=new HashSet<int>(SelectedIndices());var c=list.SelectedItem as Segment;C<TextBox>("In").Text=Segment.Clock(c==null?0:c.Start);C<TextBox>("Out").Text=Segment.Clock(c==null?0:c.End);timeline.InvalidateVisual();}
  void SelectClip(int index,bool jump){selectionAnchor=index;SelectMany(new[]{index});if(jump&&index>=0&&index<clips.Count)seek.Value=Segment.Flatten(new[]{clips[index]}).First().Start;Refresh();}
  void SyncScroll(){if(timeline==null)return;syncingScroll=true;var bar=C<ScrollBar>("TimelineScroll");bar.Maximum=Math.Max(0,timeline.Duration-timeline.Span);bar.ViewportSize=timeline.Span;bar.SmallChange=Math.Max(.01,timeline.Span/20);bar.LargeChange=timeline.Span*.8;bar.Value=timeline.ViewStart;bar.IsEnabled=Ready&&bar.Maximum>0;syncingScroll=false;}
  void SetRange(int index,double start,double end){if(!Ready||index<0||index>=clips.Count||clips[index].IsGroup)return;var old=clips[index];double pos=(old.Position??0)+start-old.Start;if(!Arrangement.Fits(clips,index,pos,end-start)){Status("다른 조각과 겹치거나 타임라인 시작을 넘을 수 없습니다.");return;}syncingSelection=true;clips[index]=new Segment{Start=start,End=end,Position=pos};list.SelectedIndex=index;syncingSelection=false;SyncSelection();Refresh();}
  void Mark(bool start){if(!SingleEditable)return;int index=list.SelectedIndex;var clip=clips[index];double t=Math.Round(seek.Value*info.Fps)/info.Fps;double a=start?t:clip.Start,b=start?clip.End:t;if(a<0||b>info.Duration+.001||b-a<1/info.Fps-.000001){Status("선택 구간 안에 재생 막대를 놓고 I / O를 눌러주세요.");return;}Remember();SetRange(index,a,Math.Min(b,info.Duration));Status(start?"선택 구간이 여기부터 시작합니다.":"선택 구간이 여기까지 저장됩니다.");}
  static double ParseTime(string s) {double d;TimeSpan t;if(double.TryParse(s,NumberStyles.Float,Engine.Inv,out d)){if(double.IsNaN(d)||double.IsInfinity(d))throw new Exception("유효한 시간을 입력하세요.");return d;}if(TimeSpan.TryParse(s,Engine.Inv,out t))return t.TotalSeconds;throw new Exception("시간은 00:01:23.456 또는 초 단위 숫자로 입력하세요.");}
  void ApplyTimes(){if(!SingleEditable)return;try{double a=ParseTime(C<TextBox>("In").Text),b=ParseTime(C<TextBox>("Out").Text);if(a<0||b-a<1/info.Fps-.000001||b>info.Duration+.001)throw new Exception("구간은 한 프레임 이상이어야 하며, 영상 길이를 넘을 수 없습니다.");Remember();SetRange(list.SelectedIndex,a,Math.Min(b,info.Duration));}catch(Exception e){Error(e);}}
  void AddOriginal(){if(!Ready)return;Remember();syncingSelection=true;clips.Add(new Segment{Start=0,End=info.Duration});syncingSelection=false;SelectClip(clips.Count-1,true);Status("원본 구간을 추가했습니다. 양끝 손잡이를 드래그해서 줄여주세요.");}
  void DeleteClip(){if(!Ready)return;var selected=SelectedIndices();if(selected.Count==0)return;Remember();int i=selected[0];syncingSelection=true;foreach(int index in selected.OrderByDescending(n=>n))clips.RemoveAt(index);syncingSelection=false;SelectClip(Math.Min(i,clips.Count-1),false);Status(clips.Count==0?"남은 구간이 없습니다. Ctrl+Z로 삭제를 되돌리거나 영상을 다시 열어주세요.":"삭제했습니다. 다른 조각의 위치는 유지됩니다. 빈칸은 검정 화면과 무음으로 저장됩니다.");}
  void SplitClip(){
   if(!Ready)return;if(gapPreview){Status("빈칸은 분할할 수 없습니다. 영상 조각 안에 재생 막대를 놓으세요.");return;}if(list.SelectedItems.Count>1||(list.SelectedItem!=null&&((Segment)list.SelectedItem).IsGroup)){Status("분할하려면 한 구간을 선택하세요. 연결한 구간은 합치기 해제로 나눌 수 있어요.");return;}double at=Math.Round(seek.Value*info.Fps)/info.Fps,frame=1/info.Fps;int i=list.SelectedIndex;
   Func<int,bool> inside=n=>n>=0&&n<clips.Count&&!clips[n].IsGroup&&at-clips[n].Start>=frame-.000001&&clips[n].End-at>=frame-.000001;
   if(!inside(i)){i=-1;for(int n=0;n<clips.Count;n++)if(inside(n)){i=n;break;}}
   if(i<0){Status("구간 안으로 재생 막대를 옮긴 뒤 S를 눌러주세요.");return;}
   if(playing)Toggle();Remember();var c=clips[i];syncingSelection=true;clips[i]=new Segment{Start=c.Start,End=at,Position=c.Position};clips.Insert(i+1,new Segment{Start=at,End=c.End,Position=c.Position+at-c.Start});syncingSelection=false;SelectClip(i+1,false);Status("분할 완료 · 조각이 "+clips.Count+"개가 되었습니다. 테두리가 밝은 오른쪽 조각이 선택됐어요.");
  }
  void Move(int delta){if(!Ready||list.SelectedItems.Count!=1)return;int i=list.SelectedIndex,j=i+delta;if(i<0||j<0||j>=clips.Count)return;Remember();int left=Math.Min(i,j),right=Math.Max(i,j);var a=clips[left];var b=clips[right];double start=a.Position.Value,gap=b.Position.Value-(a.Position.Value+a.Duration);b.Position=start;a.Position=start+b.Duration+Math.Max(0,gap);var selected=clips[i];syncingSelection=true;SortClips();syncingSelection=false;SelectClip(clips.IndexOf(selected),false);}  Settings Options() {
   double fps=info==null?60:info.Fps;string value=C<ComboBox>("Fps").Text.Trim();if(value!="원본 유지" && (!double.TryParse(value,NumberStyles.Float,Engine.Inv,out fps)||double.IsNaN(fps)||double.IsInfinity(fps)||fps<1||fps>1000))throw new Exception("FPS는 1~1000 사이 숫자 또는 '원본 유지'로 입력하세요.");
   return new Settings{Ratio=C<ComboBox>("Ratio").Text,Mode=C<ComboBox>("Fit").SelectedIndex==2?"늘림":C<ComboBox>("Fit").SelectedIndex==1?"채우기":"여백",Zoom=C<Slider>("FrameZoom").Value,OffsetX=frameOffsetX,OffsetY=frameOffsetY,Resolution=C<ComboBox>("Resolution").Text,Fps=fps,X=C<Slider>("CropX").Value,Y=C<Slider>("CropY").Value,Encoder=C<ComboBox>("Encoder").SelectedIndex==1?"NVIDIA":"CPU"};
  }
  void Refresh() {
   if(root==null)return;Arrangement.Normalize(clips);bool ready=Ready;foreach(string id in new[]{"Save","Play","MarkIn","MarkOut","Add","Update","Remove","Up","Down","Split","ZoomIn","ZoomOut","ZoomAll"})C<Button>(id).IsEnabled=ready;
   C<Button>("Save").IsEnabled=ready&&clips.Count>0;C<Button>("ExportConfirm").IsEnabled=ready&&clips.Count>0;
   foreach(string id in new[]{"MarkIn","MarkOut","Update","Remove"})C<Button>(id).IsEnabled=ready&&list.SelectedIndex>=0;
   C<Button>("Up").IsEnabled=ready&&list.SelectedIndex>0;C<Button>("Down").IsEnabled=ready&&list.SelectedIndex>=0&&list.SelectedIndex<clips.Count-1;
   C<Button>("Undo").IsEnabled=ready&&undo.Count>0;C<Button>("Redo").IsEnabled=ready&&redo.Count>0;
   timeline.IsEnabled=ready;timeline.SelectedIndex=list.SelectedIndex;timeline.InvalidateVisual();SyncScroll();
   C<Button>("Open").IsEnabled=!busy&&!loading&&!previewBusy;list.IsEnabled=ready;seek.IsEnabled=ready;
   foreach(string id in new[]{"Ratio","Fit","Resolution","Fps","Encoder"})C<ComboBox>(id).IsEnabled=!busy&&!loading&&!previewBusy&&!resultMode;
   C<TextBox>("In").IsEnabled=ready;C<TextBox>("Out").IsEnabled=ready;C<Slider>("CropX").IsEnabled=ready;C<Slider>("CropY").IsEnabled=ready;
   C<StackPanel>("CropPanel").Visibility=C<ComboBox>("Fit").SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
   double duration=Arrangement.Length(clips);
   C<TextBlock>("Total").Text=clips.Count==0?"저장할 구간이 없습니다":clips.Count+"개 구간 · 합계 "+Segment.Clock(duration);
   RefreshGrouping();RefreshFeatures();RefreshEditView();try{var s=Options();C<TextBlock>("FpsHint").Text=info!=null&&s.Fps>info.Fps+.01?"원본보다 높습니다. 프레임을 반복하며 보간하지 않습니다.":"원본 유지 또는 원하는 FPS로 저장합니다.";if(info!=null){int[] z=Engine.Size(info,s);C<TextBlock>("Output").Text=z[0]+" × "+z[1]+"  ·  "+s.Fps.ToString("0.###")+" fps\n"+Segment.Clock(duration)+"  ·  H.264 / MP4";}Preview();}
   catch{C<TextBlock>("FpsHint").Text="FPS에 1~1000 사이의 숫자를 입력하세요.";C<Button>("ExportConfirm").IsEnabled=false;}
   UpdateTime();
  }
  void UpdateTime(){UpdateEditCursor();C<TextBlock>("Position").Text="타임라인 "+Segment.Clock(editTrack==null?0:editTrack.CursorTime)+" / "+Segment.Clock(Arrangement.Length(clips));if(timeline!=null){timeline.Playhead=seek.Value;timeline.InvalidateVisual();}}  void Preview(){PreviewFraming();}
  void Keys(object sender,KeyEventArgs e){
   e.Handled=Shortcut(e.Key,Keyboard.Modifiers,Keyboard.FocusedElement is TextBox||Keyboard.FocusedElement is ComboBox||Keyboard.FocusedElement is Slider);
  }
  bool Shortcut(Key key,ModifierKeys modifiers,bool editingText){if(C<Border>("ExportOverlay").Visibility==Visibility.Visible){if(key==Key.Escape){CloseExportSettings();return true;}return false;}
   if((modifiers&ModifierKeys.Control)!=0){if((modifiers&ModifierKeys.Shift)!=0&&key==Key.S){SaveProjectDialog();return true;}if((modifiers&ModifierKeys.Shift)!=0&&key==Key.O){var opening=OpenProjectDialog();return true;}if(key==Key.O){OpenDialog();return true;}if(key==Key.S){var task=Export();return true;}if(editingText)return false;if(key==Key.A){if(Ready)SelectMany(Enumerable.Range(0,clips.Count));return true;}if(key==Key.J){if((modifiers&ModifierKeys.Shift)!=0)UnmergeSelected();else MergeSelected();return true;}if(key==Key.Z){Restore((modifiers&ModifierKeys.Shift)!=0);return true;}if(key==Key.Y){Restore(true);return true;}if(key==Key.B){SplitClip();return true;}return false;}
   if(!editingText&&key==Key.Escape&&frameMode){SetFraming(false);return true;}if(!editingText&&resultMode&&key==Key.Space){Toggle();return true;}if(editingText||!Ready)return false;
   if(key==Key.Escape&&timeline.Drawing){timeline.Drawing=false;timeline.CancelDrawing();Refresh();}else if(key==Key.Space)Toggle();else if(key==Key.S)SplitClip();else if(key==Key.I)Mark(true);else if(key==Key.O)Mark(false);else if(key==Key.Delete||key==Key.Back)DeleteClip();else if(key==Key.Left||key==Key.Right){if(playing)Toggle();double step=(modifiers&ModifierKeys.Shift)!=0?1:1/info.Fps;SeekTrack(Math.Max(0,editTrack.CursorTime+(key==Key.Left?-step:step)));}else return false;
   return true;
  }
  void Cancel(){if(!busy)return;cancelled=true;try{if(encoding!=null&&!encoding.HasExited)encoding.Kill();}catch{}Status("저장을 취소하는 중…");}
  bool cancelled;
  void CloseExportSettings(){C<Border>("ExportOverlay").Visibility=Visibility.Collapsed;Focus();}
  Task Export(){
   if(info==null||busy||loading||previewBusy||clips.Count==0)return Task.FromResult(0);
   if(playing)Toggle();if(resultMode)LeaveResult();Refresh();
   C<Border>("ExportOverlay").Visibility=Visibility.Visible;C<ComboBox>("Ratio").Focus();return Task.FromResult(0);
  }
  async Task StartExport(){
   if(info==null||busy||loading||previewBusy||clips.Count==0)return;
   Settings settings;try{settings=Options();}catch(Exception e){Error(e);return;}
   var d=new SaveFileDialog{Title="클립 저장",Filter="MP4 영상|*.mp4",DefaultExt=".mp4",AddExtension=true,FileName=Path.GetFileNameWithoutExtension(source)+"_clip.mp4",InitialDirectory=Path.GetDirectoryName(source),OverwritePrompt=true};if(d.ShowDialog(this)!=true)return;
   if(string.Equals(Path.GetFullPath(source),Path.GetFullPath(d.FileName),StringComparison.OrdinalIgnoreCase)){Error(new Exception("원본과 다른 이름으로 저장하세요."));return;}
   CloseExportSettings();if(resultPlaying)ToggleResult();if(playing)Toggle();busy=true;cancelled=false;Refresh();C<Button>("Cancel").Visibility=Visibility.Visible;C<ProgressBar>("Progress").Value=0;Status("MP4 저장 중…");
   var selected=clips.Select(c=>c.Copy()).ToList();double total=Arrangement.Length(selected);
   partial=Path.Combine(Path.GetDirectoryName(d.FileName),".clipdock-"+Guid.NewGuid().ToString("N")+".mp4");filterFile=Path.Combine(Path.GetTempPath(),"clipdock-"+Guid.NewGuid().ToString("N")+".txt");errorLog.Clear();
   try{
    File.WriteAllText(filterFile,Engine.Filter(info,settings,selected),new UTF8Encoding(false));
    encoding=new Process{StartInfo=new ProcessStartInfo(Engine.Tool("ffmpeg"),Engine.Arguments(source,partial,filterFile,info,settings,selected)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardErrorEncoding=Encoding.UTF8,StandardOutputEncoding=Encoding.UTF8}};
    encoding.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null){lock(errorLog){errorLog.AppendLine(e.Data);if(errorLog.Length>24000)errorLog.Remove(0,12000);}}};
    encoding.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null&&e.Data.StartsWith("out_time_us=")){double us;if(double.TryParse(e.Data.Substring(12),NumberStyles.Float,Engine.Inv,out us)&&!closed)Dispatcher.BeginInvoke(new Action(delegate{if(!busy)return;double percent=Math.Max(0,Math.Min(99.9,us/1000000/total*100));C<ProgressBar>("Progress").Value=percent;Status("MP4 저장 중 · "+percent.ToString("0.0")+"% · "+settings.Fps.ToString("0.###")+" fps");}));}};
    encoding.Start();encoding.BeginErrorReadLine();encoding.BeginOutputReadLine();await Task.Run(()=>encoding.WaitForExit());
    if(cancelled){if(!closed)Status("저장을 취소했습니다.");return;}
    if(encoding.ExitCode!=0){string log;lock(errorLog)log=errorLog.ToString();string logPath=Path.Combine(Engine.Base,"last-export-error.txt");try{File.WriteAllText(logPath,log,Encoding.UTF8);}catch{}throw new Exception("저장하지 못했습니다. "+(settings.Encoder=="NVIDIA"?"NVIDIA 인코더 지원을 확인하거나 CPU로 바꿔 다시 저장하세요.":"저장 공간과 영상 파일을 확인하세요.")+"\n\n상세 기록: "+logPath+"\n"+string.Join("\n",log.Split('\n').Reverse().Take(7).Reverse()));}
    if(!File.Exists(partial)||new FileInfo(partial).Length==0)throw new Exception("출력 파일을 생성하지 못했습니다.");
    if(File.Exists(d.FileName))File.Replace(partial,d.FileName,null);else File.Move(partial,d.FileName);
    if(!closed){C<ProgressBar>("Progress").Value=100;Status("저장 완료 · "+d.FileName);if(MessageBox.Show(this,"클립을 저장했습니다.\n"+d.FileName+"\n\n파일 위치를 열까요?","저장 완료",MessageBoxButton.YesNo,MessageBoxImage.Information)==MessageBoxResult.Yes)Process.Start("explorer.exe","/select,"+Engine.Q(d.FileName));}
   }catch(Exception e){if(!closed&&!cancelled)Error(e);}finally{if(encoding!=null){encoding.Dispose();encoding=null;}foreach(string f in new[]{partial,filterFile})try{if(f!=null&&File.Exists(f))File.Delete(f);}catch{}busy=false;if(!closed){C<Button>("Cancel").Visibility=Visibility.Collapsed;Refresh();}}
  }
  public void Snapshot(string path){UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
  public async Task Demo(string path){await Load(path);if(info==null)throw new Exception("Demo load failed");C<ComboBox>("Ratio").SelectedIndex=1;C<ComboBox>("Fps").Text="240";syncingSelection=true;clips.Clear();clips.Add(new Segment{Start=info.Duration/12,End=info.Duration*5/12});clips.Add(new Segment{Start=info.Duration*7/12,End=info.Duration*11/12});syncingSelection=false;SelectClip(1,false);seek.Value=info.Duration*.7;Refresh();}
  public async Task TestEditing(string path,string report){C<Expander>("SourcePanel").IsExpanded=true;
   var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);passed.Add("PASS: "+name);};
   await Load(path);UpdateLayout();check(info!=null&&clips.Count==1&&list.SelectedIndex==0,"Open starts with selected full clip");
   timeline.BeginInteraction(new Point(timeline.X(0),60));timeline.MoveInteraction(new Point(timeline.X(.5),60));timeline.EndInteraction();
   check(Math.Abs(clips[0].Start-.5)<.000001,"Left handle trims and preserves frame precision");
   timeline.BeginInteraction(new Point(timeline.X(info.Duration),60));timeline.MoveInteraction(new Point(timeline.X(2.5),60));timeline.EndInteraction();
   check(Math.Abs(clips[0].End-2.5)<.000001,"Right handle trims");
   seek.Value=1.5;Shortcut(Key.S,ModifierKeys.None,false);check(clips.Count==2&&Math.Abs(clips[0].End-1.5)<.000001&&clips[1].Start==clips[0].End,"S splits at playhead without gaps");
   Shortcut(Key.Delete,ModifierKeys.None,false);check(clips.Count==1&&Math.Abs(clips[0].End-1.5)<.000001,"Delete removes selected right clip");
   Shortcut(Key.Z,ModifierKeys.Control,false);check(clips.Count==2&&list.SelectedIndex==1,"Ctrl+Z restores deleted clip and selection");
   Shortcut(Key.Z,ModifierKeys.Control,false);check(clips.Count==1&&Math.Abs(clips[0].End-2.5)<.000001,"Undo restores pre-split range");
   Shortcut(Key.Y,ModifierKeys.Control,false);check(clips.Count==2,"Redo restores split");
   timeline.BeginInteraction(new Point(timeline.X(clips[1].End),60));timeline.MoveInteraction(new Point(timeline.X(0),60));timeline.EndInteraction();
   check(Math.Abs(clips[1].End-clips[1].Start-1/info.Fps)<.000001,"Crossed handles clamp to one frame");
   Shortcut(Key.Z,ModifierKeys.Control,false);seek.Value=1.75;Shortcut(Key.I,ModifierKeys.None,false);check(Math.Abs(clips[1].Start-1.75)<.000001,"I trims selected start directly");
   seek.Value=2.25;Shortcut(Key.O,ModifierKeys.None,false);check(Math.Abs(clips[1].End-2.25)<.000001,"O trims selected end directly");
   double before=seek.Value;Shortcut(Key.Right,ModifierKeys.None,false);check(Math.Abs(seek.Value-before-1/info.Fps)<.000001,"Arrow advances exactly one source frame");
   var ranges=clips.Select(c=>c.Copy()).ToList();Move(-1);check(Math.Abs(clips[0].Start-1.75)<.000001,"Save order can be rearranged");Restore(false);
   timeline.Zoom(2,1.5);double anchor=timeline.Time(timeline.X(1.5));check(Math.Abs(anchor-1.5)<.000001&&timeline.Span<info.Duration,"Zoom preserves coordinate mapping");timeline.ResetView();
   int count=clips.Count;Shortcut(Key.S,ModifierKeys.None,true);check(clips.Count==count,"Text entry does not trigger split shortcut");
   while(clips.Count>0){SelectClip(0,false);DeleteClip();}check(!C<Button>("Save").IsEnabled,"Deleting all clips disables export instead of saving original");
   Restore(false);Restore(false);check(clips.Count==2,"Undo recovers all deleted clips");
   int history=undo.Count,redoHistory=redo.Count;double unchanged=clips[list.SelectedIndex].Start;
   timeline.BeginInteraction(new Point(timeline.X(unchanged),60));timeline.MoveInteraction(new Point(timeline.X(unchanged),60));timeline.EndInteraction();
   check(undo.Count==history&&redo.Count==redoHistory,"Clicking a handle preserves undo and redo history");
   AddOriginal();SelectClip(0,false);timeline.BeginInteraction(new Point(timeline.X(1),60));timeline.EndInteraction();
   check(list.SelectedIndex==0,"Overlapping clip hit testing selects visible top clip");Restore(false);
   timeline.Zoom(8,1);seek.Value=2.8;Shortcut(Key.Right,ModifierKeys.None,false);
   check(seek.Value>=timeline.ViewStart&&seek.Value<=timeline.ViewStart+timeline.Span,"Frame stepping reveals offscreen playhead");timeline.ResetView();
   bool rejected=false;try{ParseTime("NaN");}catch{rejected=true;}check(rejected,"Invalid nonfinite time is rejected");
   rejected=false;try{Engine.Filter(info,new Settings(),new List<Segment>{new Segment{Start=double.NaN,End=1}});}catch{rejected=true;}check(rejected,"Export rejects invalid numeric ranges");
   File.WriteAllText(report,string.Join(Environment.NewLine,passed)+Environment.NewLine,Encoding.UTF8);
   File.WriteAllText(report+".ranges",string.Join(",",ranges.Select(c=>Engine.N(c.Start)+":"+Engine.N(c.End))),Encoding.UTF8);
   SelectClip(1,false);seek.Value=2;Status("드래그 · 분할 · 삭제 · 실행 취소 테스트 통과");
  }
 }
 public static class Program {
  [STAThread] public static int Main(string[] args){
   try{
    if(args.Length>0&&args[0]=="--test-export"){
     if(args.Length<8)throw new Exception("--test-export input output ratio mode fps ranges [resolution]");
     var v=Engine.Probe(args[1]);var s=new Settings{Ratio=args[3],Mode=args[4],Fps=double.Parse(args[5],Engine.Inv),Resolution=args.Length>7?args[7]:"원본 크기 기준"};
     var ranges=args[6].Split(',').Select(x=>{var p=x.Split(':');return new Segment{Start=double.Parse(p[0],Engine.Inv),End=double.Parse(p[1],Engine.Inv)};}).ToList();
     string script=args[2]+".filter.txt";File.WriteAllText(script,Engine.Filter(v,s,ranges),new UTF8Encoding(false));
     var p2=Process.Start(new ProcessStartInfo(Engine.Tool("ffmpeg"),Engine.Arguments(args[1],args[2],script,v,s,ranges)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true});var read=p2.StandardOutput.ReadToEndAsync();string err=p2.StandardError.ReadToEnd();p2.WaitForExit();File.WriteAllText(args[2]+".log",err);File.Delete(script);return p2.ExitCode;
    }
    var app=new Application();var editor=new Editor();editor.Automated=args.Length>0;if(args.Length>1&&args[0]=="--snapshot"){editor.Loaded+=async delegate{if(args.Length>2)await editor.Demo(args[2]);var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1200)};t.Tick+=delegate{t.Stop();editor.Snapshot(args[1]);editor.Close();};t.Start();};}
    if(args.Length>3&&args[0]=="--test-editor"){editor.Loaded+=async delegate{try{await editor.TestEditing(args[1],args[2]);var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1000)};t.Tick+=delegate{t.Stop();editor.Snapshot(args[3]);editor.Close();};t.Start();}catch(Exception e){File.WriteAllText(args[2],e.ToString());app.Shutdown(1);}};}
    if(args.Length>3&&args[0]=="--test-features"){editor.Loaded+=async delegate{try{await editor.TestFeatures(args[1],args[2]);var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1000)};t.Tick+=delegate{t.Stop();editor.Snapshot(args[3]);editor.Close();};t.Start();}catch(Exception e){File.WriteAllText(args[2],e.ToString());app.Shutdown(1);}};}
    if(args.Length>3&&args[0]=="--test-grouping"){editor.Loaded+=async delegate{try{await editor.TestGrouping(args[1],args[2]);var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1000)};t.Tick+=delegate{t.Stop();editor.Snapshot(args[3]);editor.Close();};t.Start();}catch(Exception e){File.WriteAllText(args[2],e.ToString());app.Shutdown(1);}};}
    if(args.Length>3&&args[0]=="--test-editview"){editor.Loaded+=async delegate{try{await editor.TestEditView(args[1],args[2]);var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1000)};t.Tick+=delegate{t.Stop();editor.Snapshot(args[3]);editor.Close();};t.Start();}catch(Exception e){File.WriteAllText(args[2],e.ToString());app.Shutdown(1);}};}
    if(args.Length>3&&args[0]=="--test-framing"){editor.Loaded+=async delegate{try{await editor.TestFraming(args[1],args[2]);var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1000)};t.Tick+=delegate{t.Stop();editor.Snapshot(args[3]);editor.Close();};t.Start();}catch(Exception e){File.WriteAllText(args[2],e.ToString());app.Shutdown(1);}};}
    return app.Run(editor);
   }catch(Exception e){File.WriteAllText(Path.Combine(Engine.Base,"startup-error.txt"),e.ToString());if(args.Length==0)MessageBox.Show(e.Message,"ClipDock");return 1;}
  }
 }
}
