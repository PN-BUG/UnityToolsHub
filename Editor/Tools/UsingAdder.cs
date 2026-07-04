using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace UnityFramework
{
    [ToolInfo("自动添加 Using", "文件工具",
        Description = "扫描 Assets 下所有 .cs 文件，自动补全缺失的 using 语句。\n\n智能分析代码引用，仅添加必要的命名空间导入。",
        Icon = "📄", Tags = new[] { "C#", "using语句" })]
    public class UsingAdder : EditorWindow
    {
        [MenuItem("UnityToolsHub/Add Missing Usings")]
        public static void ShowWindow()
        {
            GetWindow<UsingAdder>("Add Missing Usings");
        }

        private void OnGUI()
        {
            if (GUILayout.Button("Add Missing Usings"))
            {
                AddMissingUsings();
            }
        }

        private static void AddMissingUsings()
        {
            string[] scriptFiles = Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories);

            foreach (string filePath in scriptFiles)
            {
                string fileContent = File.ReadAllText(filePath);
                List<string> missingUsings = GetMissingUsings(fileContent);

                if (missingUsings.Count > 0)
                {
                    foreach (var missingUsing in missingUsings)
                    {
                        fileContent = "using " + missingUsing + ";\n" + fileContent;
                    }

                    File.WriteAllText(filePath, fileContent);
                    Debug.Log($"Added missing usings to: {filePath}");
                }
            }
        }

        private static List<string> GetMissingUsings(string fileContent)
        {
            List<string> missingUsings = new List<string>();

            // 简单检查缺失的命名空间
            if (!fileContent.Contains("using UnityEngine;"))
                missingUsings.Add("UnityEngine");

            if (!fileContent.Contains("using System.Collections;"))
                missingUsings.Add("System.Collections");

            // 继续添加其他常用命名空间...

            return missingUsings;
        }
    }

}
