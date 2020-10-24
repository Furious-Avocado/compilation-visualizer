﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Needle.CompilationVisualizer
{
    /// Extended by hybridherbst / NeedleTools
    /// Based on ideas by karjj1 and angularsen:
    /// https://gist.github.com/karljj1/9c6cce803096b5cd4511cf0819ff517b
    /// https://gist.github.com/angularsen/7a48f47beb0f8a65dd786ec38b02da57/revisions
    [InitializeOnLoad]
    internal class CompilationAnalysis
    {
        private const string EditorPrefStore = "Needle.CompilationVisualizer.CompilationData";

        private const string AllowLoggingPrefsKey = nameof(CompilationAnalysis) + "_" + nameof(AllowLogging); 
        private const string ShowAssemblyReloadsPrefsKey = nameof(CompilationAnalysis) + "_" + nameof(ShowAssemblyReloads); 
        public static bool AllowLogging {
            get => EditorPrefs.HasKey(AllowLoggingPrefsKey) ? EditorPrefs.GetBool(AllowLoggingPrefsKey) : false;
            set => EditorPrefs.SetBool(AllowLoggingPrefsKey, value);
        }
        public static bool ShowAssemblyReloads {
            get => EditorPrefs.HasKey(ShowAssemblyReloadsPrefsKey) ? EditorPrefs.GetBool(ShowAssemblyReloadsPrefsKey) : true;
            set => EditorPrefs.SetBool(ShowAssemblyReloadsPrefsKey, value);
        }

        static CompilationAnalysis() {
            #if UNITY_2019_1_OR_NEWER
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            #endif
            CompilationPipeline.assemblyCompilationStarted += OnAssemblyCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnCompilationStarted(object o) {
            if(AllowLogging) Debug.Log("Compilation Started at " + DateTime.Now);
            
            // check if this is very shortly after a previous compilation and assume this is an iterative compilation
            var data = CompilationData.Get();
            var isNewCompilation = true;
            if(data != null) {
                // check time difference
                var timeSpan = (DateTime.Now - data.AfterAssemblyReload);
                if (timeSpan.TotalSeconds < 5)
                    isNewCompilation = false;
                // Debug.Log("Time since last assembly reload: " + timeSpan + "; is compiling: " + EditorApplication.isCompiling);
            }
            
            data = new CompilationData {
                CompilationStarted = DateTime.Now,
                CompilationFinished = DateTime.MinValue
            };
            
            if(isNewCompilation)
                CompilationData.Clear();
            
            CompilationData.Add();
            CompilationData.Write(data);
        }

        private static void OnCompilationFinished(object o) {
            if(AllowLogging) Debug.Log("Compilation Finished at " + DateTime.Now);
            var data = CompilationData.Get();
            #if UNITY_2019_1_OR_NEWER
            data.CompilationFinished = DateTime.Now;
            #else
            // we need to guess: last end time of all compiled assemblies
            data.CompilationFinished = data.compilationData.Max(x => x.EndTime);
            #endif
            CompilationData.Write(data);
        }
        
        private static void OnAssemblyCompilationStarted(string assembly)
        {
            var data = CompilationData.Get();
            // need to detect compilation start manually
            #if !UNITY_2019_1_OR_NEWER
            if (data == null || data.compilationFinished != DateTime.MinValue)
            {
                // this should be the start of a new compilation
                OnCompilationStarted(null);
                data = CompilationData.Get();
            }
            #endif
            // var compilationData = data.compilationData.FirstOrDefault(x => x.assembly == assembly);
            // if(compilationData == null)
            // {
                var compilationData = new CompilationData.AssemblyCompilationData() {
                    assembly = assembly,
                    StartTime = DateTime.Now
                };
                data.compilationData.Add(compilationData);
            // }
            if(AllowLogging) Debug.Log("Compilation started: " + "<b>" + assembly + "</b>" + " at " + DateTime.Now);
            compilationData.StartTime = DateTime.Now;
            CompilationData.Write(data);
        }

        private static void OnAssemblyCompilationFinished(string assembly, CompilerMessage[] arg2)
        {
            var data = CompilationData.Get();
            var compilationData = data.compilationData.LastOrDefault(x => x.assembly == assembly);
            if(compilationData == null) {
                Debug.LogError("Compilation finished for " + assembly + ", but no startTime found!");
                return;
            }

            if (AllowLogging) Debug.Log("Compilation finished: " + "<b>" + assembly + "</b>" + " at " + DateTime.Now);
            if(AllowLogging && arg2 != null)
                foreach(var arg in arg2)
                    Debug.Log(arg.type + " / " + arg.message + " (Message for " + assembly + ")");
            
            compilationData.EndTime = DateTime.Now;
            CompilationData.Write(data);
        }

        private static void OnBeforeAssemblyReload()
        {
            if(AllowLogging) Debug.Log("Before Assembly Reload at " + DateTime.Now);
            var data = CompilationData.Get();
            data.BeforeAssemblyReload = DateTime.Now;
            CompilationData.Write(data);
        }

        private static void OnAfterAssemblyReload() {
            var data = CompilationData.Get();
            if (data == null) return;
            
            #if !UNITY_2019_1_OR_NEWER
            // manual compilation end check
            if (data.CompilationFinished == DateTime.MinValue)
            {
                OnCompilationFinished(null);
                data = CompilationData.Get();
            }
            #endif
            
            data.AfterAssemblyReload = DateTime.Now;
            CompilationData.Write(data);

            if (!AllowLogging) return;
            
            Debug.Log("After Assembly Reload at " + DateTime.Now);
            
            var compilationSpan = data.CompilationFinished - data.CompilationStarted;
            var sb = new StringBuilder();
            sb.AppendLine("<b>Compilation Report</b> - Total Time: " + compilationSpan);
            foreach (var d in data.compilationData) {
                sb.AppendLine(d.ToString());
            }
            Debug.Log(sb);

            var span = data.AfterAssemblyReload - data.BeforeAssemblyReload;
            Debug.Log("<b>Assembly Reload</b> - Total Time: " + span);
        }

        [Serializable]
        public class IterativeCompilationData
        {
            public List<CompilationData> iterations = new List<CompilationData>();
            public CompilationData Current => iterations.LastOrDefault();
        }
        
        [Serializable]
        public class CompilationData : ISerializationCallbackReceiver
        {
            public SerializableDateTime
                compilationStarted,
                compilationFinished,
                beforeAssemblyReload,
                afterAssemblyReload;

            public DateTime CompilationStarted { get; set; }
            public DateTime CompilationFinished { get; set; }
            public DateTime BeforeAssemblyReload { get; set; }
            public DateTime AfterAssemblyReload { get; set; }

            [Serializable]
            public struct SerializableDateTime
            {
                private static string format = "MM-dd-yyyy HH:mm:ss.fff";
                public string utc;
                
                public DateTime DateTime {
                    get
                    {
                        if (DateTime.TryParseExact(utc, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime result))
                            return result;
                        
                        return DateTime.Now;
                    }
                    set => utc = value.ToString(format, CultureInfo.InvariantCulture);
                }

                public static implicit operator SerializableDateTime(DateTime dateTime) {
                    var sd = new SerializableDateTime { DateTime = dateTime };
                    return sd;
                }
                
                public static implicit operator DateTime(SerializableDateTime dateTime) {
                    return dateTime.DateTime;
                }
            }
            
            [Serializable]
            public class AssemblyCompilationData : ISerializationCallbackReceiver
            {
                private static string format = "HH:mm:ss.fff";
                public override string ToString() {
                    return assembly + ": " + (EndTime - StartTime) + " (from " + StartTime.ToString(format, CultureInfo.CurrentCulture) + " to " + EndTime.ToString(format, CultureInfo.CurrentCulture) + ")";
                }
                
                public string assembly;
                public SerializableDateTime startTime;
                public SerializableDateTime endTime;
                public DateTime StartTime { get; set; }
                public DateTime EndTime { get; set; }
                
                public void OnBeforeSerialize()
                {
                    startTime = StartTime;
                    endTime = EndTime;
                }

                public void OnAfterDeserialize()
                {
                    StartTime = startTime;
                    EndTime = endTime;
                }
            }
            
            public List<AssemblyCompilationData> compilationData = new List<AssemblyCompilationData>();

            private static IterativeCompilationData tempData = null;
            public static IterativeCompilationData GetAll() {
                IterativeCompilationData CreateNew()
                {
                    var sd = new IterativeCompilationData();
                    if (sd.iterations == null)
                        sd.iterations = new List<CompilationData>();
                    if(sd.iterations.Count < 1)
                        sd.iterations.Add(new CompilationData());
                    WriteAll(sd);
                    return sd;
                }
                
                if (tempData != null && tempData.iterations.Any())
                    return tempData;
                
                if (!EditorPrefs.HasKey(EditorPrefStore))
                    CreateNew();

                try {
                    var restoredData = JsonUtility.FromJson<IterativeCompilationData>(EditorPrefs.GetString(EditorPrefStore));
                    if (restoredData.iterations == null) restoredData.iterations = new List<CompilationData>();
                    if (restoredData.iterations.Count < 1) restoredData.iterations.Add(new CompilationData());
                    tempData = restoredData;
                }
                catch {
                    tempData = CreateNew();
                }
                
                return tempData;
            }

            public static CompilationData Get() {
                return GetAll().iterations.LastOrDefault();
            }

            public static void Write(CompilationData data) {
                var all = GetAll();
                all.iterations[all.iterations.Count - 1] = data;
                WriteAll(all);
            }

            public static void WriteAll(IterativeCompilationData data) {
                tempData = data;
                var json = JsonUtility.ToJson(data, true);
                EditorPrefs.SetString(EditorPrefStore, json);
            }

            public void OnBeforeSerialize()
            {
                compilationStarted = CompilationStarted;
                compilationFinished = CompilationFinished;
                afterAssemblyReload = AfterAssemblyReload;
                beforeAssemblyReload = BeforeAssemblyReload;
            }

            public void OnAfterDeserialize()
            {
                CompilationStarted = compilationStarted;
                CompilationFinished = compilationFinished;
                AfterAssemblyReload = afterAssemblyReload;
                BeforeAssemblyReload = beforeAssemblyReload;
            }

            public static void Clear()
            {
                // need to do a full clear in case a locked window wants to hold onto the old data
                tempData = new IterativeCompilationData();
            }

            public static void Add()
            {
                tempData.iterations.Add(new CompilationData());
            }
        }
        
    }
}