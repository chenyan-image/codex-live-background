using System;using System.IO;using System.Threading;
internal static class GuardWorkerFixture {static void Main(){string root=AppDomain.CurrentDomain.BaseDirectory;File.AppendAllText(Path.Combine(root,"fixture-starts.txt"),"started\n");while(!File.Exists(Path.Combine(root,"stop-guard")))Thread.Sleep(20);}}
