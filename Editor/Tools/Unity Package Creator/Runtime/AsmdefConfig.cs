/*************************************************************************
 *  Copyright © 2023-2030 FXB CO.,LTD. All rights reserved.
 *------------------------------------------------------------------------
 *  公司：DefaultCompany
 *  项目：UnityPackageCreator
 *  文件：AsmdefClass.cs
 *  作者：Administrator
 *  日期：2024/11/20 20:50:52
 *  功能：Nothing
*************************************************************************/

using System.Collections.Generic;

namespace UnityPackageCreator.Runtime
{
    public class AsmdefConfig
    {
        public string name;
        public string rootNamespace;
        public List<string> references;
        public List<string> includePlatforms;
        public List<string> excludePlatforms;
        public bool allowUnsafeCode;
        public bool overrideReferences;
        public List<string> precompiledReferences;
        public bool autoReferenced;
        public List<string> defineConstraints;
        public List<string> versionDefines;
        public bool noEngineReferences;
    }
}


