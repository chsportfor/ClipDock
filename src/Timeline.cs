using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClipDock {
 public class Timeline : FrameworkElement {
  public IList<Segment> Clips;
  public int SelectedIndex=-1;
  public HashSet<int> SelectedIndices=new HashSet<int>();
  public bool Drawing;
  double drawStart,drawEnd;
  public double Duration, Fps=60, Playhead;
  public double ViewStart, ViewSpan;
  public List<ThumbnailFrame> Frames=new List<ThumbnailFrame>();
  public bool FramesLoading;
  public event Action<double> SeekRequested;
  public event Action<int,ModifierKeys> SelectRequested;
  public event Action<double,double> NewRangeRequested;
  public event Action<int,double,double> TrimRequested;
  public event Action EditStarted, EditFinished, ViewChanged;
  int drag; int dragIndex=-1; double originalStart,originalEnd,pressX; bool edited;
  const double Left=18, TrackTop=35, TrackHeight=52;
  static Brush B(string hex){return (Brush)new BrushConverter().ConvertFromString(hex);}
  static readonly Brush Back=B("#0D1420"),Muted=B("#8FA2BF"),Blue=B("#234B78"),Selected=B("#2860A0"),Accent=B("#78C5FF"),White=B("#EFF6FF");
  public Timeline(){Focusable=true;ClipToBounds=true;MinHeight=118;ToolTip="양끝 손잡이: 구간 길이 조절 · 막대 클릭/드래그: 탐색 · 휠: 확대/축소 · S: 분할";}
  public double Span {get{return ViewSpan>0?ViewSpan:Math.Max(.001,Duration);}}
  double TrackWidth {get{return Math.Max(1,ActualWidth-Left*2);}}
  public double X(double t){return Left+(t-ViewStart)/Span*TrackWidth;}
  public double Time(double x){double t=ViewStart+(x-Left)/TrackWidth*Span;return Snap(t);}
  double Snap(double t){return Math.Max(0,Math.Min(Duration,Math.Round(t*Math.Max(1,Fps))/Math.Max(1,Fps)));}
  public void ResetView(){ViewStart=0;ViewSpan=0;InvalidateVisual();if(ViewChanged!=null)ViewChanged();}
  public void Reveal(double time){if(time>=ViewStart&&time<=ViewStart+Span)return;ViewStart=Math.Max(0,Math.Min(Duration-Span,time-Span*.5));InvalidateVisual();if(ViewChanged!=null)ViewChanged();}
  public void Zoom(double factor,double anchor){double before=Span;double next=Math.Max(Math.Min(Duration,.25),Math.Min(Duration,before/factor));double fraction=(anchor-ViewStart)/before;ViewSpan=next;ViewStart=Math.Max(0,Math.Min(Duration-next,anchor-fraction*next));InvalidateVisual();if(ViewChanged!=null)ViewChanged();}
  void Text(DrawingContext dc,string text,double x,double y,Brush color,double size){dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Malgun Gothic"),size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));}
  protected override void OnRender(DrawingContext dc){
   base.OnRender(dc);dc.DrawRoundedRectangle(Back,null,new Rect(0,0,ActualWidth,ActualHeight),7,7);
   if(Duration<=0){Text(dc,"영상을 열면 여기에 편집 막대가 표시됩니다.",20,47,Muted,12);return;}
   double raw=Span/Math.Max(2,TrackWidth/95),power=Math.Pow(10,Math.Floor(Math.Log10(raw))),unit=raw/power;
   double step=(unit<=1?1:unit<=2?2:unit<=5?5:10)*power;
   for(double t=Math.Ceiling(ViewStart/step)*step;t<=ViewStart+Span+.00001;t+=step){double x=X(t);dc.DrawLine(new Pen(B("#28374C"),1),new Point(x,27),new Point(x,93));string label=Span<10?t.ToString("0.00",CultureInfo.InvariantCulture)+"s":TimeSpan.FromSeconds(t).ToString(t>=3600?@"hh\:mm\:ss":@"mm\:ss");Text(dc,label,Math.Min(ActualWidth-49,Math.Max(4,x-13)),6,Muted,10);}
   dc.DrawRoundedRectangle(B("#182235"),null,new Rect(Left,TrackTop,TrackWidth,TrackHeight),4,4);
   dc.PushClip(new RectangleGeometry(new Rect(Left,TrackTop,TrackWidth,TrackHeight)));
   foreach(var frame in Frames){double x=X(frame.Start),width=X(frame.End)-x;if(width>0&&x+width>=Left&&x<=ActualWidth-Left){dc.PushOpacity(.45);dc.DrawImage(frame.Image,new Rect(x,TrackTop,width,TrackHeight));dc.Pop();}}
   dc.Pop();
   if(Clips!=null){for(int i=0;i<Clips.Count;i++)if(!SelectedIndices.Contains(i))DrawClip(dc,i,false);for(int i=0;i<Clips.Count;i++)if(SelectedIndices.Contains(i))DrawClip(dc,i,true);}
   if(drag==4){double a=Math.Max(Left,X(Math.Min(drawStart,drawEnd))),b=Math.Min(ActualWidth-Left,X(Math.Max(drawStart,drawEnd)));if(b>a)dc.DrawRectangle(B("#6678C5FF"),new Pen(White,2),new Rect(a,TrackTop,b-a,TrackHeight));}
   if(Playhead>=ViewStart&&Playhead<=ViewStart+Span){double x=X(Playhead);dc.DrawLine(new Pen(White,1.5),new Point(x,27),new Point(x,96));var g=new StreamGeometry();using(var c=g.Open()){c.BeginFigure(new Point(x-5,22),true,true);c.LineTo(new Point(x+5,22),true,false);c.LineTo(new Point(x,29),true,false);}dc.DrawGeometry(White,null,g);}
   Text(dc,Drawing?"남길 구간을 드래그하세요 · Esc 취소":"Ctrl / Shift 클릭으로 여러 구간 선택 · 파란 구간만 저장",18,99,Drawing?Accent:Muted,10);
   if(FramesLoading)Text(dc,"장면 불러오는 중…",Math.Max(260,ActualWidth-125),99,Accent,10);else if(Span<Duration-.001)Text(dc,"휠로 확대 / 축소",Math.Max(260,ActualWidth-115),99,Accent,10);
  }
  void DrawClip(DrawingContext dc,int index,bool selected){
   var clip=Clips[index];var parts=Segment.Flatten(new[]{clip});for(int j=0;j<parts.Count;j++)DrawPiece(dc,parts[j],index,selected,clip.IsGroup,j,parts.Count);
  }
  void DrawPiece(DrawingContext dc,Segment c,int index,bool selected,bool grouped,int part,int count){
   double left=Math.Max(Left,X(c.Start)),right=Math.Min(ActualWidth-Left,X(c.End));if(right<=left)return;
   dc.DrawRoundedRectangle(Frames.Count==0?(selected?Selected:Blue):B(selected?"#663B87D0":"#44234B78"),new Pen(selected?Accent:B("#4376AE"),selected?2:1),new Rect(left,TrackTop,right-left,TrackHeight),4,4);
   if(right-left>60){double width=Math.Min(grouped?85:53,right-left-12);dc.DrawRoundedRectangle(B("#BB101722"),null,new Rect(left+6,TrackTop+14,width,23),3,3);Text(dc,grouped?(index+1)+"묶음 · "+(part+1)+"/"+count:(index+1)+"번 구간",left+9,TrackTop+17,White,10);}
   if(selected&&!grouped&&SelectedIndices.Count==1){foreach(double t in new[]{c.Start,c.End})if(t>=ViewStart&&t<=ViewStart+Span){double x=X(t);dc.DrawRoundedRectangle(Accent,null,new Rect(x-6,TrackTop-3,12,TrackHeight+6),3,3);dc.DrawLine(new Pen(B("#18456C"),2),new Point(x,TrackTop+16),new Point(x,TrackTop+36));}}
  }
  int HandleAt(Point p){if(Drawing||SelectedIndices.Count!=1||SelectedIndex<0||Clips==null||SelectedIndex>=Clips.Count||Clips[SelectedIndex].IsGroup||p.Y<TrackTop-7||p.Y>TrackTop+TrackHeight+7)return 0;var c=Clips[SelectedIndex];double a=Math.Abs(p.X-X(c.Start)),b=Math.Abs(p.X-X(c.End));if(Math.Min(a,b)>10)return 0;return a<=b?1:2;}
  public bool BeginInteraction(Point p,ModifierKeys modifiers=ModifierKeys.None){
   if(!IsEnabled||Duration<=0)return false;
   if(Drawing){drag=4;drawStart=drawEnd=Time(p.X);InvalidateVisual();return true;}
   drag=modifiers==ModifierKeys.None?HandleAt(p):0;pressX=p.X;edited=false;
   if(drag>0){dragIndex=SelectedIndex;originalStart=Clips[dragIndex].Start;originalEnd=Clips[dragIndex].End;}
   else {drag=3;if(p.Y>=TrackTop&&p.Y<=TrackTop+TrackHeight&&Clips!=null){double time=Time(p.X);int index=-1;for(int i=0;i<Clips.Count;i++)if(Contains(i,time))index=i;foreach(int i in SelectedIndices.OrderBy(i=>i))if(Contains(i,time))index=i;if(index>=0&&SelectRequested!=null)SelectRequested(index,modifiers);}if(SeekRequested!=null)SeekRequested(Time(p.X));}
   return true;
  }
  public void MoveInteraction(Point p){
   if(drag==0)return;if(drag==4){drawEnd=Time(p.X);InvalidateVisual();if(SeekRequested!=null)SeekRequested(drawEnd);return;}if(drag!=3&&!edited&&Math.Abs(p.X-pressX)<.000001)return;double time=Time(p.X),frame=1/Math.Max(1,Fps);
   if(drag==1){double start=Math.Max(0,Math.Min(originalEnd-frame,time));if(Math.Abs(start-Clips[dragIndex].Start)<.0000001)return;StartEdit();if(TrimRequested!=null)TrimRequested(dragIndex,start,originalEnd);if(SeekRequested!=null)SeekRequested(start);}
   else if(drag==2){double end=Math.Min(Duration,Math.Max(originalStart+frame,time));if(Math.Abs(end-Clips[dragIndex].End)<.0000001)return;StartEdit();if(TrimRequested!=null)TrimRequested(dragIndex,originalStart,end);if(SeekRequested!=null)SeekRequested(Math.Max(originalStart,end-frame));}
   else if(SeekRequested!=null)SeekRequested(time);
  }
  void StartEdit(){if(edited)return;edited=true;if(EditStarted!=null)EditStarted();}
  bool Contains(int index,double time){return index>=0&&index<Clips.Count&&Segment.Flatten(new[]{Clips[index]}).Any(c=>time>=c.Start&&time<c.End);}
  public void CancelDrawing(){Drawing=false;drag=0;edited=false;InvalidateVisual();}
  public void EndInteraction(){bool drawing=drag==4;bool editing=edited;drag=0;dragIndex=-1;edited=false;if(drawing&&Math.Abs(drawEnd-drawStart)>=1/Math.Max(1,Fps)-.000001&&NewRangeRequested!=null)NewRangeRequested(Math.Min(drawStart,drawEnd),Math.Max(drawStart,drawEnd));if(editing&&EditFinished!=null)EditFinished();InvalidateVisual();}
  protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){base.OnMouseLeftButtonDown(e);Focus();if(BeginInteraction(e.GetPosition(this),Keyboard.Modifiers)){CaptureMouse();e.Handled=true;}}
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);var p=e.GetPosition(this);Cursor=Drawing?Cursors.Cross:HandleAt(p)>0?Cursors.SizeWE:Cursors.Hand;if(IsMouseCaptured)MoveInteraction(p);}
  protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){base.OnMouseLeftButtonUp(e);if(IsMouseCaptured){MoveInteraction(e.GetPosition(this));EndInteraction();ReleaseMouseCapture();e.Handled=true;}}
  protected override void OnLostMouseCapture(MouseEventArgs e){base.OnLostMouseCapture(e);EndInteraction();}
  protected override void OnMouseWheel(MouseWheelEventArgs e){base.OnMouseWheel(e);if(Duration>0){Zoom(e.Delta>0?1.5:1/1.5,Time(e.GetPosition(this).X));e.Handled=true;}}
 }
}
