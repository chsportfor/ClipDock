using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClipDock {
 public class FramePlacement {public int Width,Height;public double Left,Top;}
 public static partial class Engine {
  public static void ValidateTransform(Settings s){
   if(!Projects.Finite(s.Zoom)||s.Zoom<.1||s.Zoom>5||!Projects.Finite(s.OffsetX)||!Projects.Finite(s.OffsetY)||Math.Abs(s.OffsetX)>4||Math.Abs(s.OffsetY)>4)throw new Exception("영상 크기 또는 위치가 올바르지 않습니다.");
  }
  public static FramePlacement Placement(VideoInfo v,Settings s){
   ValidateTransform(s);int[] size=Size(v,s);
   double fit=s.Mode=="채우기"?Math.Max((double)size[0]/v.Width,(double)size[1]/v.Height):Math.Min((double)size[0]/v.Width,(double)size[1]/v.Height);
   int w=Math.Max(2,(int)Math.Round((s.Mode=="늘림"?size[0]:v.Width*fit)*s.Zoom/2)*2);
   int h=Math.Max(2,(int)Math.Round((s.Mode=="늘림"?size[1]:v.Height*fit)*s.Zoom/2)*2);
   double left=(size[0]-w)*(s.Mode=="채우기"?s.X:.5)+s.OffsetX*size[0];
   double top=(size[1]-h)*(s.Mode=="채우기"?s.Y:.5)+s.OffsetY*size[1];
   return new FramePlacement{Width=w,Height=h,Left=Math.Round(left/2)*2,Top=Math.Round(top/2)*2};
  }
 }
 public partial class Editor {
  class FrameState {public double Zoom,X,Y,CropX,CropY;public int Fit,Ratio,Resolution;}
  double frameOffsetX,frameOffsetY;
  bool frameMode,frameDragging,frameMoved,syncingFrameCanvas;
  Point framePress;double framePressX,framePressY;
  FrameState CaptureFrame(){return new FrameState{Zoom=C<Slider>("FrameZoom").Value,X=frameOffsetX,Y=frameOffsetY,CropX=C<Slider>("CropX").Value,CropY=C<Slider>("CropY").Value,Fit=C<ComboBox>("Fit").SelectedIndex,Ratio=C<ComboBox>("Ratio").SelectedIndex,Resolution=C<ComboBox>("Resolution").SelectedIndex};}
  void RestoreFrame(FrameState state){if(state==null)return;frameOffsetX=state.X;frameOffsetY=state.Y;C<Slider>("FrameZoom").Value=state.Zoom;C<Slider>("CropX").Value=state.CropX;C<Slider>("CropY").Value=state.CropY;C<ComboBox>("Fit").SelectedIndex=state.Fit;C<ComboBox>("Ratio").SelectedIndex=state.Ratio;C<ComboBox>("Resolution").SelectedIndex=state.Resolution;}
  void ResetFraming(){frameOffsetX=frameOffsetY=0;C<Slider>("FrameZoom").Value=1;SetFraming(false);}
  void SetFraming(bool active){
   frameMode=active&&info!=null&&!busy&&!loading;
   C<Border>("FrameTools").Visibility=frameMode?Visibility.Visible:Visibility.Collapsed;
   C<Canvas>("Viewport").Cursor=frameMode?Cursors.Hand:Cursors.Arrow;
   C<Button>("FrameToggle").Content=frameMode?"화면 조절 닫기":"화면 조절";
   if(!frameMode&&frameDragging){frameDragging=false;C<Canvas>("Viewport").ReleaseMouseCapture();}
   Preview();
  }
  void SetFramePreset(int mode){
   if(!Ready)return;Remember();frameOffsetX=frameOffsetY=0;C<Slider>("FrameZoom").Value=1;C<Slider>("CropX").Value=C<Slider>("CropY").Value=.5;C<ComboBox>("Fit").SelectedIndex=mode;Refresh();
  }
  void FrameBegin(Point p){if(!Ready||!frameMode)return;if(playing)PauseLive();frameDragging=true;frameMoved=false;framePress=p;framePressX=frameOffsetX;framePressY=frameOffsetY;}
  void FrameMove(Point p){
   if(!frameDragging||!Ready)return;var canvas=C<Canvas>("Viewport");if(canvas.Width<=0||canvas.Height<=0)return;
   if(!frameMoved){if((p-framePress).Length<3)return;Remember();frameMoved=true;}
   frameOffsetX=Math.Max(-4,Math.Min(4,framePressX+(p.X-framePress.X)/canvas.Width));frameOffsetY=Math.Max(-4,Math.Min(4,framePressY+(p.Y-framePress.Y)/canvas.Height));Refresh();
  }
  void FrameEnd(){frameDragging=false;if(frameMoved)Status("영상 위치를 변경했습니다. 저장할 때 같은 위치가 적용됩니다. Ctrl+Z로 되돌릴 수 있어요.");}
  void InitializeFraming(){
   Fill("FrameCanvas",new[]{"1920 × 1080  ·  가로","1080 × 1920  ·  세로","1920 × 1920  ·  정사각형","원본 화면","기타 (저장 설정)"});
   Bind("FrameToggle",delegate{SetFraming(!frameMode);});
   Bind("FrameFit",delegate{SetFramePreset(0);});Bind("FrameFill",delegate{SetFramePreset(1);});Bind("FrameStretch",delegate{SetFramePreset(2);});Bind("FrameReset",delegate{SetFramePreset(0);});
   C<ComboBox>("FrameCanvas").SelectionChanged+=delegate{
    if(syncingFrameCanvas||!Ready)return;int i=C<ComboBox>("FrameCanvas").SelectedIndex;if(i==4)return;Remember();frameOffsetX=frameOffsetY=0;C<Slider>("FrameZoom").Value=1;
    C<ComboBox>("Ratio").SelectedIndex=i==0?1:i==1?2:i==2?4:0;
    C<ComboBox>("Resolution").SelectedIndex=i==3?0:2;Refresh();
   };
   C<Slider>("FrameZoom").PreviewMouseLeftButtonDown+=delegate{if(Ready)Remember();};
   C<Slider>("FrameZoom").PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(Ready&&(e.Key==Key.Left||e.Key==Key.Right||e.Key==Key.Up||e.Key==Key.Down))Remember();};
   C<Slider>("FrameZoom").ValueChanged+=delegate{Refresh();};
   var canvas=C<Canvas>("Viewport");
   canvas.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(!frameMode)return;FrameBegin(e.GetPosition(canvas));if(frameDragging){canvas.CaptureMouse();e.Handled=true;}};
   canvas.MouseMove+=delegate(object sender,MouseEventArgs e){if(frameDragging)FrameMove(e.GetPosition(canvas));};
   canvas.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){if(!frameDragging)return;FrameEnd();canvas.ReleaseMouseCapture();e.Handled=true;};
   canvas.LostMouseCapture+=delegate{FrameEnd();};
   canvas.MouseWheel+=delegate(object sender,MouseWheelEventArgs e){
    if(!frameMode||!Ready)return;Remember();Point p=e.GetPosition(canvas);var old=Engine.Placement(info,Options());int[] size=Engine.Size(info,Options());double scale=canvas.Width/size[0],u=(p.X/scale-old.Left)/old.Width,v=(p.Y/scale-old.Top)/old.Height;
    C<Slider>("FrameZoom").Value*=e.Delta>0?1.1:1/1.1;var next=Engine.Placement(info,Options());
    frameOffsetX=Math.Max(-4,Math.Min(4,frameOffsetX+(p.X/scale-u*next.Width-next.Left)/size[0]));frameOffsetY=Math.Max(-4,Math.Min(4,frameOffsetY+(p.Y/scale-v*next.Height-next.Top)/size[1]));Refresh();e.Handled=true;
   };
  }
  void PreviewFraming(){
   if(root==null)return;C<Button>("FrameToggle").IsEnabled=Ready;
   if(info==null)return;
   try{
    var s=Options();int[] size=Engine.Size(info,s);var stage=C<Grid>("Stage");
    double scale=Math.Min(stage.ActualWidth/size[0],stage.ActualHeight/size[1]);if(scale<=0)return;
    var canvas=C<Canvas>("Viewport");canvas.Width=size[0]*scale;canvas.Height=size[1]*scale;var place=Engine.Placement(info,s);
    player.Width=place.Width*scale;player.Height=place.Height*scale;Canvas.SetLeft(player,place.Left*scale);Canvas.SetTop(player,place.Top*scale);
    still.Width=player.Width;still.Height=player.Height;Canvas.SetLeft(still,Canvas.GetLeft(player));Canvas.SetTop(still,Canvas.GetTop(player));
    if(resultPlayer!=null){resultPlayer.Width=canvas.Width;resultPlayer.Height=canvas.Height;Canvas.SetLeft(resultPlayer,0);Canvas.SetTop(resultPlayer,0);}
    C<TextBlock>("PreviewLabel").Text="PREVIEW  ·  "+size[0]+" × "+size[1]+"  ·  "+(s.Mode=="채우기"?"채우기":s.Mode=="늘림"?"늘림":"전체 보이기");
    C<TextBlock>("FramePercent").Text=(s.Zoom*100).ToString("0")+"%";
    syncingFrameCanvas=true;C<ComboBox>("FrameCanvas").SelectedIndex=s.Resolution=="1080p"&&s.Ratio=="16:9"?0:s.Resolution=="1080p"&&s.Ratio=="9:16"?1:s.Resolution=="1080p"&&s.Ratio=="1:1"?2:s.Resolution=="원본 크기 기준"&&s.Ratio=="원본"?3:4;syncingFrameCanvas=false;
   }catch{syncingFrameCanvas=false;}
  }
  async System.Threading.Tasks.Task ExportFrameTest(string output){
   string filter=output+".filter";System.IO.File.WriteAllText(filter,Engine.Filter(info,Options(),clips));
   using(var p=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Engine.Tool("ffmpeg"),Engine.Arguments(source,output,filter,info,Options(),clips)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){
    var stdout=p.StandardOutput.ReadToEndAsync();string err=await p.StandardError.ReadToEndAsync();await System.Threading.Tasks.Task.Run(()=>p.WaitForExit());if(p.ExitCode!=0)throw new Exception(err);
   }
  }
  async System.Threading.Tasks.Task<Rect> RedBounds(string file){
   var sourceImage=MediaJobs.Decode(await MediaJobs.Frame(file,0,System.Threading.CancellationToken.None));
   var image=new System.Windows.Media.Imaging.FormatConvertedBitmap(sourceImage,System.Windows.Media.PixelFormats.Rgb24,null,0);
   byte[] pixels=new byte[image.PixelWidth*image.PixelHeight*3];image.CopyPixels(pixels,image.PixelWidth*3,0);
   int left=image.PixelWidth,top=image.PixelHeight,right=-1,bottom=-1;
   for(int y=0;y<image.PixelHeight;y++)for(int x=0;x<image.PixelWidth;x++){int at=(y*image.PixelWidth+x)*3;if(pixels[at]>150&&pixels[at+1]<80&&pixels[at+2]<80){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}}
   if(right<0)throw new Exception("No red content in rendered test frame");return new Rect(left,top,right-left+1,bottom-top+1);
  }
  public async System.Threading.Tasks.Task TestFraming(string input,string report){
   var passed=new System.Collections.Generic.List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);passed.Add("PASS: "+name);};
   await Load(input);UpdateLayout();SetFraming(true);C<ComboBox>("FrameCanvas").SelectedIndex=0;SetFramePreset(0);Refresh();UpdateLayout();
   check(Engine.Size(info,Options()).SequenceEqual(new[]{1920,1080}),"Full HD canvas is independent from 640 by 360 recording resolution");
   C<Slider>("FrameZoom").Value=2;Refresh();var placement=Engine.Placement(info,Options());var canvas=C<Canvas>("Viewport");
   check(placement.Width==3840&&placement.Height==2160&&placement.Left==-960&&placement.Top==-540,"200 percent zoom crops embedded borders around centered content");
   check(Math.Abs(player.Width/canvas.Width-2)<.001&&Math.Abs(Canvas.GetLeft(player)/canvas.Width+.5)<.001,"Live preview uses exact export transform");
   var press=new Point(canvas.Width*.5,canvas.Height*.5);FrameBegin(press);FrameMove(new Point(press.X+canvas.Width*.1,press.Y-canvas.Height*.1));FrameEnd();
   check(Math.Abs(frameOffsetX-.1)<.000001&&Math.Abs(frameOffsetY+.1)<.000001,"Dragging video changes normalized horizontal and vertical position");
   Restore(false);check(frameOffsetX==0&&frameOffsetY==0&&C<Slider>("FrameZoom").Value==2,"One undo restores entire video drag");
   C<Slider>("FrameZoom").Value=1.4;frameOffsetX=.1;frameOffsetY=-.05;Refresh();string project=report+".clipdock";Projects.Write(project,CaptureProject());await LoadProject(project,null);
   check(Math.Abs(Options().Zoom-1.4)<.000001&&Options().OffsetX==.1&&Options().OffsetY==-.05,"Project round-trip preserves zoom and position");
   var json=new System.Web.Script.Serialization.JavaScriptSerializer();var legacy=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(json.Serialize(CaptureProject()));legacy["Version"]=3;
   var old=(System.Collections.Generic.Dictionary<string,object>)legacy["Settings"];old.Remove("Zoom");old.Remove("OffsetX");old.Remove("OffsetY");string legacyPath=report+".legacy.clipdock";System.IO.File.WriteAllText(legacyPath,json.Serialize(legacy));var migrated=Projects.Read(legacyPath);
   check(migrated.Settings.Zoom==1&&migrated.Settings.OffsetX==0&&migrated.Settings.OffsetY==0,"Existing projects without transform fields retain original appearance");
   var bad=CaptureProject();bad.Settings.Zoom=double.NaN;bool rejected=false;try{Projects.Validate(bad,info.Duration);}catch{rejected=true;}check(rejected,"Nonfinite project transform is rejected");
   var v=new VideoInfo{Width=640,Height=480};var fill=new Settings{Ratio="16:9",Resolution="1080p",Mode="채우기"};var fit=new Settings{Ratio="16:9",Resolution="1080p"};var stretch=new Settings{Ratio="16:9",Resolution="1080p",Mode="늘림"};
   check(Engine.Placement(v,fit).Width==1440&&Engine.Placement(v,fit).Height==1080,"Fit retains complete 4:3 image with side margins");
   check(Engine.Placement(v,fill).Width==1920&&Engine.Placement(v,fill).Height==1440,"Fill covers widescreen canvas without margins");
   check(Engine.Placement(v,stretch).Width==1920&&Engine.Placement(v,stretch).Height==1080,"Stretch fills both axes when aspect distortion is intentional");
   syncingSelection=true;clips.Clear();clips.Add(new Segment{Start=0,End=.25,Position=0});syncingSelection=false;SelectClip(0,false);SetFramePreset(0);C<Slider>("FrameZoom").Value=2;C<ComboBox>("Fps").Text="240";Refresh();
   string zoomFile=report+".zoom.mp4";await ExportFrameTest(zoomFile);var red=await RedBounds(zoomFile);var exported=Engine.Probe(zoomFile);
   check(exported.Width==1920&&exported.Height==1080&&Math.Abs(exported.Fps-240)<.001,"Transformed export preserves Full HD resolution and 240fps");
   check(red.X<=1&&red.Y<=1&&red.Width>=158&&red.Height>=88,"Zoom removes black border in actual exported video");
   C<Slider>("FrameZoom").Value=.5;frameOffsetX=.1;frameOffsetY=-.1;Refresh();string shrinkFile=report+".shrink.mp4";await ExportFrameTest(shrinkFile);red=await RedBounds(shrinkFile);
   check(Math.Abs(red.X-76)<=1&&Math.Abs(red.Y-25)<=1&&Math.Abs(red.Width-40)<=2&&Math.Abs(red.Height-22.5)<=2,"Actual exported content matches scaled and shifted preview coordinates");
   SetFraming(true);C<Slider>("FrameZoom").Value=2;frameOffsetX=frameOffsetY=0;Refresh();await StillFrame();UpdateLayout();Snapshot(report+".framing.png");
   double oldWidth=Width,oldHeight=Height;Width=640;Height=600;UpdateLayout();AdaptEditorSize();UpdateLayout();
   var tools=C<Border>("FrameTools");var toolsTop=tools.TranslatePoint(new Point(0,0),C<Grid>("Stage"));check(toolsTop.Y>=0&&tools.ActualHeight<=C<Grid>("Stage").ActualHeight,"Framing controls fit inside compact preview without changing layout");
   Snapshot(report+".compact.png");Width=oldWidth;Height=oldHeight;UpdateLayout();AdaptEditorSize();UpdateLayout();
   SetFramePreset(0);check(Options().Zoom==1&&Options().OffsetX==0&&Options().OffsetY==0,"Reset restores centered original framing");
   SetFraming(false);C<Slider>("FrameZoom").Value=2;Refresh();Status("화면 크기·위치·Full HD·240fps 출력 검증 완료");
   System.IO.File.WriteAllText(report,string.Join(Environment.NewLine,passed),System.Text.Encoding.UTF8);
  } }
}
