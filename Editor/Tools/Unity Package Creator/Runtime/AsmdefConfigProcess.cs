/*************************************************************************
 *  Copyright © 2023-2030 FXB CO.,LTD. All rights reserved.
 *------------------------------------------------------------------------
 *  公司：DefaultCompany
 *  项目：UnityPackageCreator
 *  文件：AsmdefConfigProcess.cs
 *  作者：Administrator
 *  日期：2024/11/20 20:49:13
 *  功能：Nothing
*************************************************************************/

using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace UnityPackageCreator.Runtime
{
    public class AsmdefConfigProcess
    {
        /// <summary>
        /// 创建.asmdef
        /// </summary>
        /// <param name="path">创建的位置</param>
        /// <param name="packageName">包的名称：比如com.zko.unitypackagecreator</param>
        /// <param name="isEditor">适用于运行时还是编辑器下</param>
        public static void CreateAsmdefContent(string filePath, bool isEditor)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath) ;
            AsmdefConfig asmdefClass = new AsmdefConfig();
            asmdefClass.name = fileName;
            asmdefClass.rootNamespace = "";
            asmdefClass.references = new List<string>();
            asmdefClass.includePlatforms = isEditor ? new List<string> { "Editor" } : new List<string>();
            asmdefClass.excludePlatforms = new List<string>();
            asmdefClass.allowUnsafeCode = false;
            asmdefClass.overrideReferences = false;
            asmdefClass.precompiledReferences = new List<string>();
            asmdefClass.autoReferenced = true;
            asmdefClass.defineConstraints = new List<string>();
            asmdefClass.versionDefines = new List<string>();
            asmdefClass.noEngineReferences = false;

            JObject asmdefJson = JObject.FromObject(asmdefClass);

            File.WriteAllText(filePath, asmdefJson.ToString());
        }
    }
}


