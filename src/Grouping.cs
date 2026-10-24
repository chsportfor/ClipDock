using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClipDock {
 public partial class Editor {
  int selectionAnchor=-1;
  List<int> SelectedIndices(){return list.SelectedItems.Cast<Segment>().Select(c=>clips.IndexOf(c)).Where(i=>i>=0).OrderBy(i=>i).ToList();}
  bool SingleEditable {get{return Ready&&list.SelectedItems.Count==1&&list.SelectedItem!=null&&!((Segment)list.SelectedItem).IsGroup;}}
  void SelectMany(IEnumerable<int> indices){
   syncingSelection=true;list.SelectedItems.Clear();foreach(int i in indices.Distinct().OrderBy(i=>i))if(i>=0&&i<clips.Count)list.SelectedItems.Add(clips[i]);syncingSelection=false;SyncSelection();Refresh();
  }
  void SelectFromTimeline(int index,ModifierKeys modifiers){
   if(!Ready)return;var selected=SelectedIndices();
   if((modifiers&ModifierKeys.Shift)!=0&&selectionAnchor>=0){int a=Math.Min(selectionAnchor,index),b=Math.Max(selectionAnchor,index);selected=Enumerable.Range(a,b-a+1).ToList();}
   else if((modifiers&ModifierKeys.Control)!=0){if(selected.Contains(index))selected.Remove(index);else selected.Add(index);if(selectionAnchor<0)selectionAnchor=index;}
   else{selected=new List<int>{index};selectionAnchor=index;}
   SelectMany(selected);
  }
  void InitializeGrouping(){
   Bind("Merge",delegate{MergeSelected();});Bind("Unmerge",delegate{UnmergeSelected();});
   timeline.NewRangeRequested+=delegate(double a,double b){if(!Ready)return;bool first=undo.Count==0&&clips.Count==1&&!clips[0].IsGroup&&clips[0].Start==0&&clips[0].End==info.Duration;Remember();timeline.Drawing=false;syncingSelection=true;if(first)clips.Clear();clips.Add(new Segment{Start=a,End=b});syncingSelection=false;SelectClip(clips.Count-1,true);Status("새 파란 구간을 추가했습니다. Ctrl+클릭으로 여러 구간을 선택해 합칠 수 있어요.");};
  }
  void ToggleDraw(){if(!Ready)return;if(playing)Toggle();C<Expander>("SourcePanel").IsExpanded=true;UpdateLayout();timeline.Drawing=!timeline.Drawing;timeline.InvalidateVisual();Refresh();Status(timeline.Drawing?"타임라인에서 남길 부분을 드래그하세요. N 또는 Esc로 취소합니다.":"구간 그리기를 취소했습니다.");}
  void RefreshGrouping(){
   var selected=SelectedIndices();timeline.SelectedIndices=new HashSet<int>(selected);
   foreach(string name in new[]{"MarkIn","MarkOut","Update"})C<Button>(name).IsEnabled=SingleEditable;
   C<TextBox>("In").IsEnabled=SingleEditable;C<TextBox>("Out").IsEnabled=SingleEditable;
   C<Button>("Merge").IsEnabled=Ready&&selected.Count>=2;C<Button>("Unmerge").IsEnabled=Ready&&selected.Any(i=>clips[i].IsGroup);
   C<Button>("Split").IsEnabled=Ready&&selected.Count<=1&&!selected.Any(i=>clips[i].IsGroup);
   C<Button>("Up").IsEnabled=Ready&&selected.Count==1&&selected[0]>0;C<Button>("Down").IsEnabled=Ready&&selected.Count==1&&selected[0]<clips.Count-1;
   C<Button>("Add").Content=timeline.Drawing?"구간 그리기 취소  N":"+ 구간 그리기  N";
   if(selected.Count>1)C<TextBlock>("Total").Text=selected.Count+"개 선택 · 전체 "+Segment.Clock(Arrangement.Length(clips));
   timeline.InvalidateVisual();
  }
  void MergeSelected(){
   if(!Ready)return;var indices=SelectedIndices();if(indices.Count<2){Status("Ctrl 또는 Shift를 누르고 합칠 구간을 여러 개 선택하세요.");return;}
   double begin=indices.Min(i=>clips[i].Position.Value),end=indices.Max(i=>clips[i].Position.Value+clips[i].Duration);
   if(clips.Where((c,i)=>!indices.Contains(i)).Any(c=>c.Position<end&&c.Position+c.Duration>begin)){Status("사이에 다른 조각이 있습니다. 사이 조각도 선택하거나 먼저 이동하세요.");return;}
   Remember();var parts=Arrangement.Leaves(indices.Select(i=>clips[i]),0);foreach(var part in parts)part.Position-=begin;
   var group=new Segment{Start=parts.Min(c=>c.Start),End=parts.Max(c=>c.End),Parts=parts,Position=begin};
   syncingSelection=true;foreach(int i in indices.OrderByDescending(i=>i))clips.RemoveAt(i);clips.Add(group);SortClips();syncingSelection=false;SelectClip(clips.IndexOf(group),false);
   Status("조각과 사이 빈칸을 현재 위치 그대로 묶었습니다. 함께 이동할 수 있어요.");
  }
  void UnmergeSelected(){
   if(!Ready)return;var indices=SelectedIndices();if(!indices.Any(i=>clips[i].IsGroup))return;Remember();var next=new List<Segment>();var selected=new List<Segment>();
   for(int i=0;i<clips.Count;i++){if(indices.Contains(i)){var parts=Arrangement.Leaves(new[]{clips[i]},0);next.AddRange(parts);selected.AddRange(parts);}else next.Add(clips[i]);}
   syncingSelection=true;clips.Clear();foreach(var c in next.OrderBy(c=>c.Position))clips.Add(c);syncingSelection=false;SelectMany(selected.Select(c=>clips.IndexOf(c)));Status("묶음을 풀었습니다. 각 조각의 위치는 유지됩니다.");
  }  public async Task TestGrouping(string input,string report){
   var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);passed.Add("PASS: "+name);};
   await Load(input);UpdateLayout();ToggleDraw();timeline.BeginInteraction(new Point(timeline.X(.1),60));timeline.MoveInteraction(new Point(timeline.X(.2),60));timeline.EndInteraction();check(clips.Count==1&&Math.Abs(clips[0].Duration-.1)<.00001,"First drawn range replaces untouched full-video selection");Restore(false);SetRange(0,.25,.75);
   ToggleDraw();timeline.BeginInteraction(new Point(timeline.X(1.25),60));timeline.MoveInteraction(new Point(timeline.X(1.75),60));timeline.EndInteraction();
   check(clips.Count==2&&clips[1].Start==1.25&&clips[1].End==1.75,"Dragging creates an additional independent blue range");
   ToggleDraw();timeline.BeginInteraction(new Point(timeline.X(2.75),60));timeline.MoveInteraction(new Point(timeline.X(2.25),60));timeline.EndInteraction();
   check(clips.Count==3&&clips[2].Start==2.25&&clips[2].End==2.75,"Reverse drag adds a correctly ordered range");
   SelectClip(0,false);timeline.BeginInteraction(new Point(timeline.X(2.5),60),ModifierKeys.Control);timeline.EndInteraction();check(SelectedIndices().SequenceEqual(new[]{0,2}),"Ctrl-click selects separated ranges");
   Shortcut(Key.J,ModifierKeys.Control,false);check(clips.Count==2&&clips[0].IsGroup&&clips[0].Parts.Count==2&&Math.Abs(clips[0].Duration-1)<.000001,"Merge creates one output block without including the gap");
   check(Segment.Flatten(clips).Select(c=>c.Start).SequenceEqual(new[]{.25,2.25,1.25}),"Nonadjacent merge preserves selected order and keeps unselected ranges");
   check(!C<Button>("MarkIn").IsEnabled&&!C<TextBox>("In").IsEnabled,"Composite cannot be accidentally expanded into excluded gaps");
   Shortcut(Key.Z,ModifierKeys.Control,false);check(clips.Count==3&&SelectedIndices().SequenceEqual(new[]{0,2}),"Undo restores original ranges and multi-selection");
   Shortcut(Key.Y,ModifierKeys.Control,false);check(clips[0].IsGroup,"Redo restores the merged block");
   Shortcut(Key.J,ModifierKeys.Control|ModifierKeys.Shift,false);check(clips.Count==3&&!clips.Any(c=>c.IsGroup)&&SelectedIndices().SequenceEqual(new[]{0,1}),"Unmerge restores individual pieces in saved order");
   Shortcut(Key.Z,ModifierKeys.Control,false);Move(1);check(clips[1].IsGroup,"Merged block moves as one unit");Move(-1);
   string project=report+".clipdock";Projects.Write(project,CaptureProject());await LoadProject(project,null);check(clips.Count==2&&clips[0].IsGroup&&clips[0].Parts[1].Start==2.25,"Project round-trip preserves merged contents");
   var legacy=CaptureProject();legacy.Version=1;legacy.Clips=Segment.Flatten(legacy.Clips).Select(c=>c.Copy()).ToList();string legacyPath=report+".legacy.clipdock";Projects.Write(legacyPath,legacy);await LoadProject(legacyPath,null);check(clips.Count==3&&!clips.Any(c=>c.IsGroup),"Version-one projects remain readable");await LoadProject(project,null);
   string key=StateKey();DeleteClip();check(clips.Count==1&&!clips[0].IsGroup,"Deleting a merged block removes all its pieces");Restore(false);check(StateKey()==key,"Undo deeply restores merged contents");
   SelectMany(new[]{0,1});DeleteClip();check(clips.Count==0&&!C<Button>("Save").IsEnabled,"Multi-delete removes all selected blocks");Restore(false);check(StateKey()==key,"Undo restores multi-deleted groups");
   SelectFromTimeline(0,ModifierKeys.None);SelectFromTimeline(1,ModifierKeys.Shift);check(SelectedIndices().Count==2,"Shift selects consecutive output blocks");MergeSelected();check(clips.Count==1&&clips[0].Parts.Count==3,"Merging existing groups flattens their pieces without nesting");
   check(Math.Abs(clips[0].Duration-1.5)<.000001,"Merged duration excludes all removed gaps");
   await ShowResult();var proxy=await Task.Run(()=>Engine.Probe(resultCachePath));check(Math.Abs(proxy.Duration-1.5)<.04,"Edited-result preview uses merged piece durations");LeaveResult();
   string output=report+".mp4",filter=output+".filter";var settings=Options();settings.Fps=240;
   File.WriteAllText(filter,Engine.Filter(info,settings,clips.ToList()),new UTF8Encoding(false));
   using(var process=new System.Diagnostics.Process{StartInfo=new System.Diagnostics.ProcessStartInfo(Engine.Tool("ffmpeg"),Engine.Arguments(source,output,filter,info,settings,clips.ToList())){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true}}){process.Start();var error=process.StandardError.ReadToEndAsync();var stdout=process.StandardOutput.ReadToEndAsync();await Task.Run(()=>process.WaitForExit());check(process.ExitCode==0,"Merged clip exports successfully");}
   File.Delete(filter);var rendered=await Task.Run(()=>Engine.Probe(output));check(Math.Abs(rendered.Duration-1.5)<.01&&Math.Abs(rendered.Fps-240)<.001,"Merged export keeps 240fps and exact total duration");
   filmTimer.Stop();await BuildFilmstrip();SelectClip(0,false);seek.Value=2.5;Status("여러 파란 구간을 하나로 연결 · 제외한 부분은 유지");
   File.WriteAllText(report,string.Join(Environment.NewLine,passed),Encoding.UTF8);
  }
 }
}



