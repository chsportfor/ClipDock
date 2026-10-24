using System;
using System.Linq;
using System.Collections.Generic;
namespace ClipDock {
 public static class Arrangement {
  public static void Normalize(IEnumerable<Segment> clips){double end=0;foreach(var c in clips){if(c.IsGroup)Normalize(c.Parts);if(!c.Position.HasValue)c.Position=end;end=Math.Max(end,c.Position.Value+c.Duration);}}
  public static double Length(IEnumerable<Segment> clips){double end=0;foreach(var c in clips){double pos=c.Position??end;end=Math.Max(end,pos+c.Duration);}return end;}
  public static bool Fits(IList<Segment> clips,int except,double pos,double duration){return pos>=-.0000001&&clips.Where((c,i)=>i!=except).All(c=>pos+duration<=(c.Position??0)+.0000001||pos>=(c.Position??0)+c.Duration-.0000001);}
  public static List<Segment> Leaves(IEnumerable<Segment> clips,double origin){var output=new List<Segment>();double cursor=0;foreach(var c in clips){double pos=c.Position??cursor;if(c.IsGroup)output.AddRange(Leaves(c.Parts,origin+pos));else{var leaf=c.Copy();leaf.Position=origin+pos;output.Add(leaf);}cursor=Math.Max(cursor,pos+c.Duration);}return output.OrderBy(c=>c.Position).ToList();}
  public static void Validate(IEnumerable<Segment> clips){
   double end=0;var copy=clips.Select(c=>c.Copy()).ToList();Normalize(copy);
   foreach(var c in copy.OrderBy(c=>c.Position)){double p=c.Position.Value;if(double.IsNaN(p)||double.IsInfinity(p)||p<0||p>86400||p<end-.000001)throw new Exception("조각의 타임라인 위치가 겹치거나 올바르지 않습니다.");if(c.IsGroup)Validate(c.Parts);end=p+c.Duration;}
  }
  public static List<Segment> Plan(IEnumerable<Segment> clips){
   Validate(clips);var output=new List<Segment>();double end=0;
   foreach(var c in Leaves(clips,0)){double pos=c.Position.Value;if(pos>end+.000001)output.Add(new Segment{Start=0,End=pos-end,IsGap=true});c.Position=null;output.Add(c);end=pos+c.Duration;}
   return output;
  }
 }
}
