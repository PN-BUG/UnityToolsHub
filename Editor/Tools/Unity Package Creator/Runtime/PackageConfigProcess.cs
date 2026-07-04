/*************************************************************************
 *  Copyright © 2023-2030 FXB CO.,LTD. All rights reserved.
 *------------------------------------------------------------------------
 *  公司：DefaultCompany
 *  项目：UnityPackageCreator
 *  文件：PackageConfigProcess.cs
 *  作者：Administrator
 *  日期：2024/11/20 21:52:26
 *  功能：Nothing
*************************************************************************/

using Newtonsoft.Json.Linq;
using System.IO;

namespace UnityPackageCreator.Runtime
{
    public class PackageConfigProcess
    {
        public static void CreatePackageJson(string path, PackageConfig packageConfig)
        {
            JObject packageConfigJson = JObject.FromObject(packageConfig);

            string fullPath = Path.Combine(path, $"package.json");

            File.WriteAllText(fullPath, packageConfigJson.ToString());
        }
    }
}


