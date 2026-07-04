/*************************************************************************
 *  Copyright © 2023-2030 FXB CO.,LTD. All rights reserved.
 *------------------------------------------------------------------------
 *  公司：DefaultCompany
 *  项目：UnityPackageCreator
 *  文件：PackageConfig.cs
 *  作者：Administrator
 *  日期：2024/11/20 21:25:18
 *  功能：Nothing
*************************************************************************/

using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityPackageCreator.Runtime
{
    [System.Serializable]
    public class Author
    {
        public string name = "";
        public string email = "";
        public string url = "";
    }

    [System.Serializable]
    public class Dependency
    { 
        public string packageName;
        public string version;
    }

    [System.Serializable]
    public class Sample
    {
        public string displayName;
        public string description;
        public string path;
    }

    public class PackageConfig
    {
        public string name = "";
        public string version = "";
        public string displayName = "";
        public string description = "";
        public string unity = "";
        public string unityRelease = "";
        public string documentationUrl = "";
        public string changelogUrl = "";
        public string licensesUrl = "";
        public JObject dependencies = new JObject();
        public List<string> keywords = new List<string>();
        public Author author = new Author();
        public List<Sample> samples = new List<Sample>();
    }
}


