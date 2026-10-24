using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
namespace ClipDock {
 public partial class Editor {
  class LiveSpan {public double At,End,Source;public bool Gap;}
  List<LiveSpan> liveSpans=new List<LiveSpan>();
  Stopwatch liveClock=new Stopwatch();
  double liveOrigin,liveCursor;
  bool liveCursorValid;
  int liveIndex=-1;
  void BuildLivePlan(){liveSpans.Clear();double t=0;foreach(var part in Arrangement.Plan(clips)){liveSpans.Add(new LiveSpan{At=t,End=t+part.Duration,Source=part.Start,Gap=part.IsGap});t+=part.Duration;}}
  int LiveIndexAt(double t){return liveSpans.FindIndex(c=>t>=c.At&&t<c.End);}
  void ResetLive(){liveClock.Reset();playing=false;liveIndex=-1;liveCursor=0;liveCursorValid=false;}
  void PauseLive(){
   if(!playing)return;
   liveCursor=Math.Min(Arrangement.Length(clips),liveOrigin+liveClock.Elapsed.TotalSeconds);
   playing=false;liveClock.Stop();player.Pause();liveCursorValid=true;
   C<Button>("Play").Content="▶ 재생";UpdateTime();ScheduleStill();
  }
  void ToggleLive(){
   if(info==null||busy||loading||clips.Count==0)return;
   if(playing){PauseLive();return;}
   BuildLivePlan();double length=Arrangement.Length(clips);
   double at=liveCursorValid?liveCursor:gapPreview?gapCursor:editTrack==null?0:editTrack.CursorTime;
   if(at>=length-1/info.Fps||at<0)at=0;
   liveCursor=at;liveCursorValid=true;liveOrigin=at;liveIndex=-1;playing=true;
   stillVersion++;stillTimer.Stop();ApplyLive(at);liveClock.Restart();
   C<Button>("Play").Content="❚❚ 일시 정지";
  }
  void ApplyLive(double t){
   liveCursor=t;liveCursorValid=true;
   int i=LiveIndexAt(t);
   if(i<0)return;
   var span=liveSpans[i];bool transition=i!=liveIndex;liveIndex=i;if(transition){int selected=editTrack.Hit(t);SelectMany(selected>=0?new[]{selected}:new int[0]);}
   if(span.Gap){
    if(transition){player.Pause();player.IsMuted=true;player.Visibility=Visibility.Hidden;still.Source=null;}
    gapPreview=true;gapCursor=t;
   }else{
    double sourceTime=span.Source+t-span.At;gapPreview=false;
    if(transition){player.Pause();player.IsMuted=false;player.Position=TimeSpan.FromSeconds(sourceTime);player.Visibility=Visibility.Visible;still.Visibility=Visibility.Hidden;if(playing)player.Play();}
    changing=true;seek.Value=Math.Min(info.Duration,sourceTime);changing=false;
   }
   UpdateTime();
   if(editTrack!=null&&playing){var scroll=C<ScrollViewer>("EditScroll");double x=16+t*editTrack.Scale;if(x>scroll.HorizontalOffset+scroll.ViewportWidth-24||x<scroll.HorizontalOffset)scroll.ScrollToHorizontalOffset(Math.Max(0,x-scroll.ViewportWidth*.25));}
  }
  void TickLive(){
   if(!playing||info==null)return;
   double t=liveOrigin+liveClock.Elapsed.TotalSeconds,total=Arrangement.Length(clips);
   if(t>=total){
    ApplyLive(Math.Max(0,total-1/info.Fps));playing=false;liveClock.Stop();player.Pause();liveCursor=total;liveCursorValid=true;C<Button>("Play").Content="▶ 재생";UpdateTime();ScheduleStill();return;
   }
   ApplyLive(t);
  }
 }
}

