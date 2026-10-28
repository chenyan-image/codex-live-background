using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

// Compiled separately and embedded in the manager. Windows provides the OCR
// engine and language data; no Python installation or model server is needed.
public static class CodexHubWindowsOcr {
    public static string Read(byte[] pixels,int width,int height,CancellationToken stop){
        if(width<1||height<1||width>4096||height>4096||pixels.Length!=width*height*4)throw new ArgumentException("Invalid OCR image.");
        var engine=OcrEngine.TryCreateFromUserProfileLanguages();
        if(engine==null)throw new NotSupportedException("Windows 文字识别语言不可用，将使用抖音控件定位。");
        using(var writer=new Windows.Storage.Streams.DataWriter()){
            writer.WriteBytes(pixels);
            using(var bitmap=SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(),BitmapPixelFormat.Bgra8,width,height,BitmapAlphaMode.Ignore)){
                var operation=engine.RecognizeAsync(bitmap);var clock=Stopwatch.StartNew();
                try{
                    while(operation.Status==Windows.Foundation.AsyncStatus.Started){stop.ThrowIfCancellationRequested();if(clock.ElapsedMilliseconds>2500)throw new TimeoutException("局部文字识别超时。");Thread.Sleep(10);}
                    var result=operation.GetResults();var words=new List<object>();int lineId=0;
                    foreach(var line in result.Lines){foreach(var word in line.Words){var r=word.BoundingRect;words.Add(new{Text=word.Text,X=r.X,Y=r.Y,Width=r.Width,Height=r.Height,Line=lineId});}lineId++;}
                    return new JavaScriptSerializer().Serialize(words);
                }finally{if(operation.Status==Windows.Foundation.AsyncStatus.Started)operation.Cancel();operation.Close();}
            }
        }
    }
}
