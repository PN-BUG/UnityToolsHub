using UnityEngine;

/// <summary>
/// 兼容旧版 Unity 的查找辅助方法
/// FindObjectOfType 在 2023.1+ 已过时，替代为 FindAnyObjectByType
/// </summary>
public static class FindHelper
{
    /// <summary>
    /// 兼容版本的 FindObjectOfType
    /// </summary>
    public static T FindAnyObject<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }

    /// <summary>
    /// 兼容版本的 FindObjectOfType（可指定查找模式）
    /// </summary>
    public static T FindAnyObject<T>(FindObjectsInactive findObjectsInactive) where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<T>(findObjectsInactive);
#else
        // 旧版 Unity 不支持 FindObjectsInactive 参数，忽略该参数
        return Object.FindObjectOfType<T>();
#endif
    }

    /// <summary>
    /// 兼容版本的 Physics2D.OverlapPointNonAlloc
    /// OverlapPointNonAlloc 在 2023.1+ 已过时，替代为 OverlapPoint
    /// </summary>
    public static int OverlapPoint(Vector2 point, Collider2D[] results, int layerMask)
    {
#if UNITY_2023_1_OR_NEWER
        var filter = new ContactFilter2D();
        filter.SetLayerMask(layerMask);
        filter.useTriggers = true;
        return Physics2D.OverlapPoint(point, filter, results);
#else
        return Physics2D.OverlapPointNonAlloc(point, results, layerMask);
#endif
    }
}
