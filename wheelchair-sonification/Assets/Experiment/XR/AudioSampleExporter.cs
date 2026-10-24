#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using WheelchairSonification.Core;
namespace WheelchairSonification.Editor
{
    public static class AudioSampleExporter
    {
        [Serializable] public sealed class Sample { public string file,title; public double seconds,peak,maxAdjacentStep; public bool clipping; }
        [Serializable] sealed class Manifest { public bool physical_headset_listened=false; public string generator="StereoToneGenerator used by UnityFeedbackOutput"; public Sample[] samples; }
        public static void Export(string directory)
        {
            var samples=new List<Sample>();
            string[] names={"left-only","right-only","both-equal","unequal","acceleration","stop","missing","stationary-noise","two-minutes"};
            string[] titles={"左だけ","右だけ","左右同速","左右の速度差","加減速","停止とフェード","欠測で消音","静止ノイズで無音","連続2分・左右変化"};
            for(int scenario=0;scenario<names.Length;scenario++)
            {
                int rate=48000,seconds=scenario==8?120:8; var generator=new StereoToneGenerator();var engine=new SpeedFeedbackEngine(new FeedbackSettings());
                var data=new float[rate*seconds*2];double peak=0,step=0;
                for(int frame=0;frame<seconds*100;frame++)
                {
                    double t=frame*.01,l=.5,r=.5;bool valid=true;
                    switch(scenario){case 0:r=0;break;case 1:l=0;break;case 3:l=.9;r=.3;break;case 4:l=r=.1+1.3*(.5-.5*Math.Cos(2*Math.PI*t/8));break;case 5:if(t>3)l=r=0;break;case 6:valid=t<3||t>5;break;case 7:l=.02*Math.Sin(t*40);r=.02*Math.Cos(t*40);break;case 8:l=.5+.25*Math.Sin(t*.4);r=.5+.25*Math.Cos(t*.3);break;}
                    // Silence at both ends makes previews safe to concatenate and checks the actual fades.
                    if(t<.1||t>seconds-.2)l=r=0;
                    var value=engine.Update(t,l,r,valid,StudyGroup.Auditory,true);var block=new float[960];generator.Render(block,2,rate,value,.1);
                    Array.Copy(block,0,data,frame*960,960);
                }
                for(int i=0;i<data.Length;i++){peak=Math.Max(peak,Math.Abs(data[i]));if(i>=2)step=Math.Max(step,Math.Abs(data[i]-data[i-2]));}
                if(peak>.3001||step>.03)throw new InvalidOperationException("音見本のクリップ・不連続："+names[scenario]);
                string file="audio-"+names[scenario]+".wav";WriteWave(Path.Combine(directory,file),data,rate);
                samples.Add(new Sample{file=file,title=titles[scenario],seconds=seconds,peak=peak,maxAdjacentStep=step,clipping=peak>=1});
            }
            File.WriteAllText(Path.Combine(directory,"audio-manifest.json"),JsonUtility.ToJson(new Manifest{samples=samples.ToArray()},true));
        }
        static void WriteWave(string path,float[] data,int rate)
        {
            using var w=new BinaryWriter(File.Create(path));w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));w.Write(36+data.Length*2);w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)2);w.Write(rate);w.Write(rate*4);w.Write((short)4);w.Write((short)16);w.Write(System.Text.Encoding.ASCII.GetBytes("data"));w.Write(data.Length*2);
            foreach(float v in data)w.Write((short)Math.Round(FeedbackProtocol.Clamp(v,-1,1)*32767));
        }
    }
}
#endif
