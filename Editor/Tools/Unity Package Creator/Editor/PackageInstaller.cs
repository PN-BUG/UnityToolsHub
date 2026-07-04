/*************************************************************************
 *  Copyright © 2023-2030 Administrator. All rights reserved.
 *------------------------------------------------------------------------
 *  公司：DefaultCompany
 *  项目：UnityPackageCreator
 *  文件：PackageInstaller.cs
 *  作者：Administrator
 *  日期：2024/11/20 21:25:18
 *  功能：Nothing
*************************************************************************/

using UnityEditor.PackageManager.Requests;
using UnityEditor.PackageManager;
using UnityEditor;
using UnityEngine;
using System.IO;

namespace UnityPackageCreator.Editor
{
    public class PackageInstaller
    {
        /// <summary>
        /// 从本地路径安装包
        /// 1、可以通过本地文件夹安装包，格式file:pathtopackagefolder，如file:E:/UPMProject/UPM/com.zko.test
        /// 2、从.tgz文件安装包，格式file:pathtopackage.tgz，如file:E:/UPMProject/UPM/com.zko.test.tgz
        /// </summary>
        /// <param name="packagePath"></param>
        public static void InstallPackageFromDisk(string packagePath)
        {
            //从本地文件夹安装包
            if (Directory.Exists(packagePath))
            {
                // 构造package.json文件的完整路径
                string packageJsonPath = Path.Combine(packagePath, "package.json");

                // 检查package.json文件是否存在
                if (!File.Exists(packageJsonPath))
                {
                    Debug.LogError("The provided folder does not contain a valid 'package.json' file and is not a valid Unity package.");
                    return;
                }
            }
            //从.tgz文件安装包
            else if (File.Exists(packagePath))
            {
                if (!packagePath.EndsWith(".tgz"))
                {
                    Debug.LogError($"{packagePath} file is not a valid Unity package.");
                    return;
                }
            }
            else
            {
                Debug.LogError($"The package at path {packagePath} does not exist.");
                return;
            }

            // 构建正确的标识符
            string identifier = $"file:{packagePath}";
            // 如果存在package.json，那么这是一个有效的包，可以继续安装
            AddRequest request = Client.Add(identifier);
            EditorApplication.CallbackFunction onUpdate = null;
            onUpdate = () =>
            {
                if (request.IsCompleted)
                {
                    if (request.Status == StatusCode.Success)
                        Debug.Log($"Package from {packagePath} installed successfully.");
                    else
                        Debug.LogError($"Failed to install package from {packagePath}: {request.Error.message}");

                    EditorApplication.update -= onUpdate;
                }
            };
            EditorApplication.update += onUpdate;
        }

        public static void InstallPackage(string packageName)
        {
            AddRequest request = Client.Add(packageName);
            EditorApplication.CallbackFunction onUpdate = null;
            onUpdate = () =>
            {
                if (request.IsCompleted)
                {
                    if (request.Status == StatusCode.Success)
                        Debug.Log($"Package {packageName} installed successfully.");
                    else
                        Debug.LogError($"Failed to install package {packageName}: {request.Error.message}");

                    EditorApplication.update -= onUpdate;
                }
            };
            EditorApplication.update += onUpdate;
        }

        public static void RemovePackage(string packageName)
        {
            RemoveRequest request = Client.Remove(packageName);
            EditorApplication.CallbackFunction onUpdate = null;
            onUpdate = () =>
            {
                if (request.IsCompleted)
                {
                    if (request.Status == StatusCode.Success)
                        Debug.Log($"Package {packageName} removed successfully.");
                    else
                        Debug.LogError($"Failed to remove package {packageName}: {request.Error.message}");

                    EditorApplication.update -= onUpdate;
                }
            };
            EditorApplication.update += onUpdate;
        }
    }
}


