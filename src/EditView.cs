using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace ClipDock {
 public class EditTrack : FrameworkElement {
  public IList<Segment> Clips;
  public HashSet<int> Selected=new HashSet<int>();
  public double CursorTime,Fps=60,SourceDuration,PixelsPerSecond=100,ViewLeft,ViewWidth=1200;
  public List<ThumbnailFrame> Frames=new List<ThumbnailFrame>();
  public Action<int,double,ModifierKeys> SeekClip;
  public Action<double> SeekOutput;
  public Action StartEdit;
  public Action<int,double,double> Trim;
  public Action FinishEdit;
  public Action<int,double> MoveClip; public Action<int> SelectOnly;
  int active=-1,edge;double press,originalStart,originalEnd,scale,originalPosition,target;bool changed,moving;ModifierKeys modifiers;
  public double Total {get{return Clips==null?0:Arrangement.Length(Clips);}}
  public double Scale {get{return PixelsPerSecond;}}
  public double Offset(int index){return Clips[index].Position??0;}
  public double SourceAt(int index,double local){foreach(var p in Arrangement.Leaves(new[]{Clips[index]},0)){double at=p.Position.Value-Offset(index);if(local>=at&&local<=at+p.Duration)return p.Start+local-at;}return -1;}
  public double OutputAt(int index,double source){foreach(var p in Arrangement.Leaves(new[]{Clips[index]},0))if(source>=p.Start&&source<=p.End)return p.Position.Value+source-p.Start;return Offset(index);}
  public int Hit(double t){for(int i=0;i<Clips.Count;i++)if(t>=Offset(i)&&t<Offset(i)+Clips[i].Duration)return i;return -1;}
  static Brush B(string s){return (Brush)new BrushConverter().ConvertFromString(s);}
  void Text(DrawingContext d,string s,double x,double y,double width,Brush color,double size){if(width<5)return;var t=new FormattedText(s,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Malgun Gothic"),size,color,1);t.MaxTextWidth=Math.Max(5,width);t.MaxLineCount=1;t.Trimming=TextTrimming.CharacterEllipsis;d.DrawText(t,new Point(x,y));}
  public EditTrack(){Focusable=true;ClipToBounds=true;ToolTip="위 눈금 드래그: 재생 위치 · 조각 드래그: 위치 이동 · 양끝 드래그: 자르기 · 휠: 확대";}
  protected override void OnRender(DrawingContext d){
   d.DrawRectangle(B("#0C111B"),null,new Rect(0,0,ActualWidth,ActualHeight));double bottom=Math.Max(90,ActualHeight-18),k=Scale;
   double step=Math.Pow(10,Math.Floor(Math.Log10(80/k)));while(step*k<75)step*=2;
   for(double t=Math.Max(0,Math.Floor(ViewLeft/k/step)*step);t<(ViewLeft+ViewWidth)/k;t+=step){double x=16+t*k;d.DrawLine(new Pen(B("#253047"),1),new Point(x,25),new Point(x,bottom+5));Text(d,TimeSpan.FromSeconds(t).ToString(t>=3600?@"hh\:mm\:ss":@"mm\:ss\.ff"),x+3,4,80,B("#90A3BF"),10);}
   if(Clips==null||Clips.Count==0){Text(d,"영상을 열거나 원본에서 구간을 추가하세요",20,58,ActualWidth-40,B("#8293AD"),13);return;}
   for(int i=0;i<Clips.Count;i++){
    var c=Clips[i];double x=16+Offset(i)*k,w=c.Duration*k;bool sel=Selected.Contains(i);if(x+w<ViewLeft||x>ViewLeft+ViewWidth)continue;
    var r=new Rect(x+1,35,Math.Max(1,w-2),bottom-35);
    d.DrawRoundedRectangle(B(i%2==0?"#264F78":"#325B87"),new Pen(sel?B("#A3E1FF"):B("#658CB8"),sel?3:1),r,4,4);
    d.PushClip(new RectangleGeometry(r));
    for(double px=Math.Max(0,Math.Floor((ViewLeft-x)/88)*88);px<Math.Min(w,ViewLeft+ViewWidth-x);px+=88){double source=SourceAt(i,Math.Min(c.Duration-.000001,px/k));var f=source<0?null:Frames.OrderBy(a=>Math.Abs((a.Start+a.End)/2-source)).FirstOrDefault();if(f!=null)d.DrawImage(f.Image,new Rect(x+px,60,88,bottom-60));else if(source<0)d.DrawRectangle(Brushes.Black,null,new Rect(x+px,60,88,bottom-60));}
    d.DrawRectangle(B("#E51A304C"),null,new Rect(x,35,w,27));
    Text(d,(i+1).ToString("00")+"  "+(c.IsGroup?"묶음":"클립")+" · "+c.Duration.ToString("0.##")+"초",x+12,40,w-24,B("#F0F6FF"),12);
    if(sel){d.DrawRectangle(B("#B276C8FF"),null,new Rect(x+3,64,5,Math.Max(10,bottom-70)));d.DrawRectangle(B("#B276C8FF"),null,new Rect(x+w-8,64,5,Math.Max(10,bottom-70)));}
    d.Pop();
   }
   if(moving&&active>=0){double delta=target-originalPosition;foreach(int i in Selected){double x=16+(Offset(i)+delta)*k;d.DrawRectangle(B("#555FA6F7"),new Pen(B("#FFD078"),2),new Rect(Math.Max(0,x),35,Math.Max(1,Clips[i].Duration*k),bottom-35));}Text(d,"이동 → "+Segment.Clock(target),16+target*k,Math.Max(65,bottom-22),240,B("#FFD078"),11);}
   double cursor=16+Math.Max(0,CursorTime)*k;d.DrawLine(new Pen(B("#FFCE78"),2),new Point(cursor,26),new Point(cursor,bottom+9));d.DrawRectangle(B("#FFCE78"),null,new Rect(cursor-5,25,10,7));
  }
  public void Begin(Point p,ModifierKeys mods){
   if(Clips==null)return;double t=Math.Max(0,(p.X-16)/Scale);active=Hit(t);press=p.X;scale=Scale;changed=false;moving=false;modifiers=mods;
   if(active<0||p.Y<35){active=-1;edge=3;if(SeekOutput!=null)SeekOutput(t);return;}
   var c=Clips[active];double x=16+Offset(active)*Scale,end=x+c.Duration*Scale;
   edge=mods==ModifierKeys.None&&!c.IsGroup?(Math.Abs(p.X-x)<8?1:Math.Abs(p.X-end)<8?2:0):0;
   originalStart=c.Start;originalEnd=c.End;originalPosition=Offset(active);target=originalPosition;
   if(SeekClip!=null)SeekClip(active,SourceAt(active,t-Offset(active)),mods);
  }
  public void MoveTo(Point p){
   if(edge==3){if(SeekOutput!=null)SeekOutput(Math.Max(0,(p.X-16)/Scale));return;}
   if(active<0)return;
   if(edge==0){if(modifiers!=ModifierKeys.None||Math.Abs(p.X-press)<4)return;moving=true;target=Math.Max(0,originalPosition+(p.X-press)/scale);target=Math.Round(target*Fps)/Fps;
    double tolerance=8/scale;foreach(var c in Clips.Where((c,i)=>!Selected.Contains(i))){foreach(double snap in new[]{c.Position.Value+c.Duration,c.Position.Value-Clips[active].Duration})if(Math.Abs(target-snap)<tolerance)target=Math.Max(0,snap);}
    if(target<tolerance)target=0;InvalidateVisual();return;}
   double delta=Math.Round((p.X-press)/scale*Fps)/Fps,frame=1/Fps;
   double a=edge==1?Math.Max(0,Math.Min(originalEnd-frame,originalStart+delta)):originalStart;
   double b=edge==2?Math.Min(SourceDuration,Math.Max(originalStart+frame,originalEnd+delta)):originalEnd;
   if(Math.Abs(a-Clips[active].Start)<.0000001&&Math.Abs(b-Clips[active].End)<.0000001)return;
   if(!changed){changed=true;if(StartEdit!=null)StartEdit();}
   if(Trim!=null)Trim(active,a,b);
  }
  public void End(){if(!moving&&!changed&&active>=0&&edge==0&&modifiers==ModifierKeys.None&&SelectOnly!=null)SelectOnly(active);if(moving&&active>=0&&MoveClip!=null)MoveClip(active,target);active=-1;edge=0;moving=false;if(changed&&FinishEdit!=null)FinishEdit();changed=false;InvalidateVisual();}
  protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){base.OnMouseLeftButtonDown(e);Focus();Begin(e.GetPosition(this),Keyboard.Modifiers);CaptureMouse();e.Handled=true;}
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(IsMouseCaptured){MoveTo(e.GetPosition(this));return;}var p=e.GetPosition(this);double t=(p.X-16)/Scale;bool handle=false;for(int i=0;i<(Clips==null?0:Clips.Count);i++)if(!Clips[i].IsGroup&&(Math.Abs(t-Offset(i))*Scale<8||Math.Abs(t-Offset(i)-Clips[i].Duration)*Scale<8))handle=true;Cursor=p.Y<35?Cursors.IBeam:handle?Cursors.SizeWE:Hit(t)>=0?Cursors.SizeAll:Cursors.Arrow;}
  protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){base.OnMouseLeftButtonUp(e);End();ReleaseMouseCapture();}
  protected override void OnLostMouseCapture(MouseEventArgs e){base.OnLostMouseCapture(e);End();}
 }
 public partial class Editor {
  EditTrack editTrack;double baseTrackScale=100;bool gapPreview;double gapCursor;
  void AdaptEditorSize(){
   if(root==null||editTrack==null)return;
   bool narrow=ActualWidth<880,shortWindow=ActualHeight<740;
   C<TextBlock>("Tagline").Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
   C<TextBlock>("TrackHint").Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
   C<TextBlock>("BottomHint").Visibility=shortWindow||narrow?Visibility.Collapsed:Visibility.Visible;
   C<TextBlock>("Meta").Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
   C<TextBlock>("Brand").FontSize=narrow?21:26;
   C<Button>("Open").Content=narrow?"영상 열기":"영상 열기   Ctrl+O";
   C<Button>("Save").Content=narrow?"MP4 저장":"MP4 저장   Ctrl+S";
   foreach(string name in new[]{"ProjectOpen","ProjectSave","Open","Save"})C<Button>(name).Padding=new Thickness(narrow?9:14,9,narrow?9:14,9);
   root.RowDefinitions[0].Height=new GridLength(shortWindow?62:76);
   root.RowDefinitions[2].Height=new GridLength(shortWindow?34:46);
   C<Grid>("EditorBody").Margin=shortWindow||narrow?new Thickness(12,10,12,8):new Thickness(20,16,20,10);
   C<Grid>("EditorRows").RowDefinitions[1].Height=new GridLength(shortWindow?46:66);
   C<ContentControl>("EditHost").Height=shortWindow?106:142;
  }  void SortClips(){var ordered=clips.OrderBy(c=>c.Position).ToList();clips.Clear();foreach(var c in ordered)clips.Add(c);}
  void SeekTrack(double t){
   if(resultMode)LeaveResult();if(!Ready)return;if(playing)Toggle();liveCursorValid=false;int index=editTrack.Hit(t);
   if(index>=0){double sourceTime=editTrack.SourceAt(index,t-editTrack.Offset(index));if(sourceTime>=0){gapPreview=false;player.Visibility=Visibility.Visible;SelectClip(index,false);seek.Value=Math.Round(sourceTime*info.Fps)/info.Fps;ScheduleStill();return;}}
   SelectMany(new int[0]);gapPreview=true;gapCursor=t;player.Pause();player.Visibility=Visibility.Hidden;still.Source=null;Refresh();
  }
  void MoveOnTrack(int index,double target){
   if(!Ready)return;var indices=SelectedIndices();if(!indices.Contains(index))indices=new List<int>{index};var items=indices.Select(i=>clips[i]).ToList();double delta=target-clips[index].Position.Value;
   if(Math.Abs(delta)<.0000001)return;
   foreach(var c in items){double p=c.Position.Value+delta;if(p<0||p+c.Duration>86400||clips.Except(items).Any(other=>p<other.Position.Value+other.Duration-.000001&&p+c.Duration>other.Position.Value+.000001)){Status("놓을 공간이 부족합니다. 빈칸으로 옮기세요. 다른 조각은 덮어쓰지 않습니다.");return;}}
   Remember();foreach(var c in items)c.Position+=delta;syncingSelection=true;SortClips();syncingSelection=false;SelectMany(items.Select(c=>clips.IndexOf(c)));Status("위치를 옮겼습니다. 다른 조각과 빈칸은 그대로 유지됩니다.");
  }
  void CloseTrackGaps(){
   if(!Ready||clips.Count==0)return;Remember();double t=0;foreach(var c in clips.OrderBy(c=>c.Position)){c.Position=t;t+=c.Duration;}Refresh();Status("빈칸을 없애고 조각을 앞에서부터 붙였습니다. Ctrl+Z로 되돌릴 수 있어요.");
  }
  void ResetTrackScale(){ResetLive();if(editTrack==null)return;gapPreview=false;double width=Math.Max(400,C<ScrollViewer>("EditScroll").ActualWidth-32);baseTrackScale=width/Math.Max(1,(info==null?10:info.Duration)*1.3);C<Slider>("TrackZoom").Value=100;C<ScrollViewer>("EditScroll").ScrollToHorizontalOffset(0);RefreshEditView();}
  void InitializeEditView(){
   editTrack=new EditTrack{Clips=clips};C<ContentControl>("EditHost").Content=editTrack;
   editTrack.SeekClip=delegate(int i,double t,ModifierKeys m){
    if(resultMode)LeaveResult();if(!Ready)return;if(playing)Toggle();
    liveCursorValid=false;if(m!=ModifierKeys.None||!SelectedIndices().Contains(i))SelectFromTimeline(i,m);
    if(t<0){gapPreview=true;gapCursor=clips[i].Position.Value;player.Visibility=Visibility.Hidden;still.Source=null;Refresh();return;}
    gapPreview=false;player.Visibility=Visibility.Visible;seek.Value=Math.Round(t*info.Fps)/info.Fps;UpdateTime();ScheduleStill();
   };
   editTrack.SelectOnly=delegate(int i){SelectClip(i,false);};editTrack.SeekOutput=SeekTrack;editTrack.MoveClip=MoveOnTrack;
   editTrack.StartEdit=delegate{Remember();};
   editTrack.Trim=delegate(int i,double a,double b){SetRange(i,a,b);seek.Value=a;};
   editTrack.FinishEdit=delegate{Status("길이를 변경했습니다. 나머지 조각은 제자리에 있습니다.");};
   Func<ContextMenu> menu=delegate{
    var cm=new ContextMenu();string[] ids={"Split","Remove","Merge","Unmerge","Up","Down","MarkIn","MarkOut","Undo","Redo"};
    string[] labels={"분할	S","삭제 (빈자리 유지)	Delete","선택 조각 묶기	Ctrl+J","묶음 풀기	Ctrl+Shift+J","앞 조각과 순서 교환","뒤 조각과 순서 교환","현재 위치를 시작으로	I","현재 위치를 끝으로	O","실행 취소	Ctrl+Z","다시 실행	Ctrl+Y"};
    for(int n=0;n<ids.Length;n++){string id=ids[n];var item=new MenuItem{Header=labels[n],IsEnabled=C<Button>(id).IsEnabled};item.Click+=delegate{C<Button>(id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};cm.Items.Add(item);}return cm;
   };
   Bind("More",delegate{var cm=menu();cm.PlacementTarget=C<Button>("More");cm.IsOpen=true;});
   editTrack.MouseRightButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(resultMode)LeaveResult();if(!Ready)return;int i=editTrack.Hit((e.GetPosition(editTrack).X-16)/editTrack.Scale);if(i>=0&&!SelectedIndices().Contains(i))SelectClip(i,false);var cm=menu();cm.PlacementTarget=editTrack;cm.IsOpen=true;e.Handled=true;};
   editTrack.MouseWheel+=delegate(object sender,MouseWheelEventArgs e){var slider=C<Slider>("TrackZoom");slider.Value*=e.Delta>0?1.25:.8;e.Handled=true;};
   Bind("TrackMinus",delegate{C<Slider>("TrackZoom").Value/=1.25;});Bind("TrackPlus",delegate{C<Slider>("TrackZoom").Value*=1.25;});
   Bind("TrackFit",delegate{baseTrackScale=Math.Max(1,C<ScrollViewer>("EditScroll").ActualWidth-32)/Math.Max(1,Arrangement.Length(clips)*1.3);C<Slider>("TrackZoom").Value=100;RefreshEditView();C<ScrollViewer>("EditScroll").ScrollToHorizontalOffset(0);});
   Bind("CloseGaps",delegate{CloseTrackGaps();});
   C<Slider>("TrackZoom").ValueChanged+=delegate{RefreshEditView();};
   C<ScrollViewer>("EditScroll").SizeChanged+=delegate{RefreshEditView();};C<ScrollViewer>("EditScroll").ScrollChanged+=delegate{editTrack.ViewLeft=C<ScrollViewer>("EditScroll").HorizontalOffset;editTrack.ViewWidth=Math.Max(400,C<ScrollViewer>("EditScroll").ViewportWidth);editTrack.InvalidateVisual();};
  }
  void RefreshEditView(){
   if(editTrack==null)return;editTrack.IsEnabled=info!=null&&!busy&&!loading&&!previewBusy;C<Button>("CloseGaps").IsEnabled=Ready&&clips.Count>0;
   editTrack.Selected=new HashSet<int>(SelectedIndices());editTrack.Fps=info==null?60:info.Fps;editTrack.SourceDuration=info==null?0:info.Duration;editTrack.Frames=timeline.Frames;
   editTrack.PixelsPerSecond=Math.Max(.001,baseTrackScale*C<Slider>("TrackZoom").Value/100);
   double viewport=Math.Max(400,C<ScrollViewer>("EditScroll").ActualWidth-2);
   C<ContentControl>("EditHost").Width=Math.Max(viewport,32+editTrack.Total*editTrack.Scale+viewport*.25);editTrack.InvalidateVisual();
  }
  void UpdateEditCursor(){if(editTrack==null)return;editTrack.CursorTime=liveCursorValid?liveCursor:resultMode?C<Slider>("ResultSeek").Value:gapPreview?gapCursor:list.SelectedIndex>=0?editTrack.OutputAt(list.SelectedIndex,seek.Value):editTrack.CursorTime;editTrack.InvalidateVisual();}  public async System.Threading.Tasks.Task TestEditView(string input,string report){
   var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);passed.Add("PASS: "+name);};
   await Load(input);UpdateLayout();double scale=editTrack.Scale;
   check(!C<Expander>("SourcePanel").IsExpanded&&editTrack.Total*scale<editTrack.ActualWidth-32,"Initial timeline has spare space instead of filling width");
   seek.Value=1;SplitClip();seek.Value=2;SplitClip();check(clips.Count==3&&clips[2].Position==2,"Split retains absolute clip placement");
   SelectClip(1,false);DeleteClip();check(clips.Count==2&&clips[1].Position==2&&editTrack.Total==3,"Delete preserves empty space and later clip position");
   check(editTrack.Scale==scale,"Deleting does not automatically zoom timeline");
   SeekTrack(1.5);check(gapPreview&&list.SelectedIndex==-1,"Clicking empty space displays black without selecting nearby clip");
   double x=16+2.5*scale;editTrack.Begin(new Point(x,75),ModifierKeys.None);editTrack.MoveTo(new Point(x-.75*scale,75));editTrack.End();
   check(Math.Abs(clips[1].Position.Value-1.25)<.00001&&clips[0].Position==0,"Dragging clip body moves it to arbitrary empty position");
   Restore(false);check(clips[1].Position==2,"One undo restores drag placement");
   MoveOnTrack(1,.5);check(clips[1].Position==2,"Overlapping drop is rejected without overwriting another clip");
   SelectClip(0,false);editTrack.Begin(new Point(18,75),ModifierKeys.None);editTrack.MoveTo(new Point(18+.25*scale,75));editTrack.End();
   check(clips[0].Position==.25&&clips[0].Start==.25&&clips[1].Position==2,"Left trim leaves a gap and does not move later clip");
   Restore(false);SelectMany(new[]{0,1});MergeSelected();check(clips.Count==1&&clips[0].IsGroup&&clips[0].Duration==3,"Grouping preserves internal empty space");
   var plan=Arrangement.Plan(clips);check(plan.Count==3&&plan[1].IsGap&&plan[1].Duration==1,"Export plan includes internal group gap");
   UnmergeSelected();check(clips.Count==2&&clips[1].Position==2,"Ungroup restores absolute positions");
   SelectMany(new[]{0,1});MoveOnTrack(0,.5);check(clips[0].Position==.5&&clips[1].Position==2.5,"Multiple selected clips move together with spacing preserved");Restore(false);
   CloseTrackGaps();check(clips[0].Position==0&&clips[1].Position==1,"Explicit close-gaps command joins clips");Restore(false);
   C<Slider>("TrackZoom").Value=200;check(Math.Abs(editTrack.Scale-scale*2)<.00001,"Zoom slider changes fixed pixels per second");C<Slider>("TrackZoom").Value=100;
   check(C<ContentControl>("EditHost").Height==142,"Timeline height is fixed");
   C<ComboBox>("Fps").Text="240";var data=CaptureProject();string project=report+".clipdock";Projects.Write(project,data);await LoadProject(project,null);
   check(clips.Count==2&&clips[1].Position==2&&C<ComboBox>("Fps").Text=="240","Project round-trip preserves gaps and FPS");
   var legacy=CaptureProject();legacy.Version=2;foreach(var c in legacy.Clips)c.Position=null;Projects.Validate(legacy,info.Duration);Arrangement.Normalize(legacy.Clips);check(legacy.Clips[1].Position==1,"Older projects migrate to contiguous timeline");
   var bad=CaptureProject();bad.Clips[1].Position=.5;bool rejected=false;try{Projects.Validate(bad,info.Duration);}catch{rejected=true;}check(rejected,"Malformed overlapping project is rejected");
   syncingSelection=true;clips.Clear();clips.Add(new Segment{Start=.25,End=.75,Position=0});clips.Add(new Segment{Start=2.25,End=2.75,Position=1});syncingSelection=false;SelectClip(1,false);Refresh();
   BuildLivePlan();check(liveSpans.Count==3&&liveSpans[1].Gap&&liveSpans[2].Source==2.25,"Live plan uses source trims and placed gaps directly");
   check(LiveIndexAt(.5)==1&&LiveIndexAt(1)==2&&LiveIndexAt(1.5)==-1,"Live cut boundaries switch to exact next span");
   SeekTrack(.05);var watch=System.Diagnostics.Stopwatch.StartNew();Toggle();check(playing&&!previewBusy&&resultCachePath==null&&watch.ElapsedMilliseconds<200,"Play starts immediately without rendering a temporary movie");
   await System.Threading.Tasks.Task.Delay(150);check(playing&&!gapPreview&&!player.IsMuted&&seek.Value>=.3&&seek.Value<.65,"Live playback reads first trimmed source region");
   await System.Threading.Tasks.Task.Delay(450);check(playing&&gapPreview&&player.IsMuted&&player.Visibility==Visibility.Hidden,"Live gap is black and silent");
   await System.Threading.Tasks.Task.Delay(500);check(playing&&!gapPreview&&!player.IsMuted&&seek.Value>=2.25&&seek.Value<2.75,"Live playback jumps to the next placed source region");
   check(list.SelectedIndex==1,"Playing clip becomes selected for immediate editing");check(player.NaturalDuration.HasTimeSpan&&player.Position.TotalSeconds>=2.25&&player.Position.TotalSeconds<2.8,"Windows player actually decodes the next trimmed source region");
   Toggle();double paused=liveCursor;await System.Threading.Tasks.Task.Delay(80);check(!playing&&Math.Abs(liveCursor-paused)<.000001,"Pause retains edited timeline position");
   Shortcut(Key.Space,ModifierKeys.None,false);check(playing,"Space resumes direct timeline playback");await System.Threading.Tasks.Task.Delay(600);check(!playing&&Math.Abs(liveCursor-1.5)<.000001,"Playback stops at edited timeline end");
   Toggle();check(playing&&liveCursor<.1,"Playing at end restarts from timeline zero");Toggle();
   SeekTrack(.7);Toggle();check(gapPreview&&player.IsMuted,"Playback can begin in an empty timeline region");Toggle();
   SeekTrack(1.1);check(!playing&&!gapPreview&&Math.Abs(seek.Value-2.35)<.005,"Seeking on timeline positions correct source without preparing a movie");
   string output=report+".mp4",filter=report+".filter";System.IO.File.WriteAllText(filter,Engine.Filter(info,Options(),clips));
   using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Engine.Tool("ffmpeg"),Engine.Arguments(source,output,filter,info,Options(),clips)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){var stdout=process.StandardOutput.ReadToEndAsync();string stderr=await process.StandardError.ReadToEndAsync();await System.Threading.Tasks.Task.Run(()=>process.WaitForExit());check(process.ExitCode==0,"Gap export succeeds: "+(process.ExitCode==0?"240fps":stderr));}
   var exported=Engine.Probe(output);check(Math.Abs(exported.Duration-1.5)<.01&&Math.Abs(exported.Fps-240)<.001,"Gap export keeps exact 1.5 seconds at 240fps");
   await BuildFilmstrip();check(editTrack.Frames.Count>=4,"Timeline thumbnails load with source panel collapsed");
   seek.Value=2.5;Refresh();Status("배치·빈칸·드래그·확대·작업 복원·240fps 저장 검증 완료");
   UpdateLayout();double stageWidth=C<Grid>("Stage").ActualWidth,stageHeight=C<Grid>("Stage").ActualHeight;
   check(stageWidth>ActualWidth*.85,"Preview uses full editor width without export sidebar");
   await Export();UpdateLayout();check(C<Border>("ExportOverlay").Visibility==Visibility.Visible,"Save opens export settings before file picker");
   check(Math.Abs(C<Grid>("Stage").ActualHeight-stageHeight)<.1,"Opening export settings does not move preview layout");
   C<ComboBox>("Fps").Text="144";check(Options().Fps==144,"Custom FPS remains editable in styled combo box");
   C<ComboBox>("Fps").Text="240";Refresh();Snapshot(report+".export.png");
   check(Shortcut(Key.Escape,ModifierKeys.None,false)&&C<Border>("ExportOverlay").Visibility==Visibility.Collapsed,"Escape returns from settings to editor");
   check(C<ComboBox>("Fps").Text=="240","Returning to editor retains export choices");
   await Export();C<ComboBox>("Fps").Text="invalid";Refresh();check(!C<Button>("ExportConfirm").IsEnabled,"Invalid FPS disables final export confirmation");CloseExportSettings();check(C<Button>("Save").IsEnabled,"Invalid FPS does not lock user out of settings");await Export();C<ComboBox>("Fps").Text="240";Refresh();CloseExportSettings();
   check(C<Expander>("SourcePanel").Visibility==Visibility.Collapsed&&!Shortcut(Key.N,ModifierKeys.None,false),"Source range panel and N drawing shortcut are removed");
   double oldWidth=Width,oldHeight=Height;Width=640;Height=600;UpdateLayout();AdaptEditorSize();UpdateLayout();
   check(ActualWidth<=641&&ActualHeight<=601&&C<Grid>("Stage").ActualHeight>=80,"Window can shrink to 640 by 600 while keeping preview visible");
   check(C<Button>("Save").IsVisible&&C<ContentControl>("EditHost").ActualHeight==106,"Compact layout retains save button and timeline");
   Snapshot(report+".compact.png");await Export();UpdateLayout();Snapshot(report+".compact-export.png");
   check(C<Button>("ExportConfirm").IsVisible&&C<Button>("ExportConfirm").TranslatePoint(new Point(0,C<Button>("ExportConfirm").ActualHeight),root).Y<root.ActualHeight,"Save settings remain accessible in compact window");
   CloseExportSettings();Width=oldWidth;Height=oldHeight;UpdateLayout();AdaptEditorSize();UpdateLayout();
   System.IO.File.WriteAllText(report,string.Join(Environment.NewLine,passed),System.Text.Encoding.UTF8);
  }
 }
}









