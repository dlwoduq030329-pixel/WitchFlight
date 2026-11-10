using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// Only used when a roster outgrows authored slots. Reuse the copies for later updates.
public static class ParticipantUiSlots
{
    public static void FitStack(IReadOnlyList<RectTransform> rows, RectTransform first, RectTransform second)
    {
        if (rows.Count <= 2 || first == null || second == null) return;
        Vector3 originalScale = first.localScale;
        float height = first.rect.height * Mathf.Abs(originalScale.y);
        float top = Mathf.Max(first.anchoredPosition.y, second.anchoredPosition.y) + height * .5f;
        float extent = Mathf.Abs(first.anchoredPosition.y - second.anchoredPosition.y) + height;
        float x = first.anchoredPosition.x;
        float cell = extent / rows.Count;
        foreach (var row in rows)
        {
            if (row == null) continue;
            var layout = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
        }
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; if (row == null) continue;
            row.pivot = new Vector2(.5f, .5f);
            row.localScale = originalScale * Mathf.Min(1f, (cell - 4f) / Mathf.Max(1f, height));
            row.anchoredPosition = new Vector2(x, top - (i + .5f) * cell);
        }
    }
    public static Transform Clone(Transform template, string name)
    {
        var copy = UnityEngine.Object.Instantiate(template.gameObject, template.parent);
        copy.name = name;
        return copy.transform;
    }
    public static T CloneView<T>(T view, Transform source, Transform copy) where T : class
        => (T)Remap(view, source, copy);
    private static object Remap(object value, Transform source, Transform copy)
    {
        if (value == null) return null;
        if (value is UnityEngine.Object obj)
        {
            Transform t = obj is GameObject go ? go.transform : obj is Component c ? c.transform : null;
            if (t == null || (t != source && !t.IsChildOf(source))) return obj;
            var indices = new Stack<int>();
            while (t != source) { indices.Push(t.GetSiblingIndex()); t = t.parent; }
            t = copy;
            while (indices.Count > 0) t = t.GetChild(indices.Pop());
            return obj is GameObject ? (object)t.gameObject : t.GetComponent(obj.GetType());
        }
        Type type = value.GetType();
        if (type.IsValueType || value is string) return value;
        if (value is Array array)
        {
            var result = Array.CreateInstance(type.GetElementType(), array.Length);
            for (int i = 0; i < array.Length; i++) result.SetValue(Remap(array.GetValue(i), source, copy), i);
            return result;
        }
        var clone = Activator.CreateInstance(type);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (!field.IsInitOnly && !field.IsNotSerialized) field.SetValue(clone, Remap(field.GetValue(value), source, copy));
        return clone;
    }
    public static void FitVertical(IReadOnlyList<RectTransform> rows, RectTransform area)
    {
        if (area == null || rows.Count == 0) return;
        float height = Mathf.Max(100f, area.rect.height - 24f);
        float width = Mathf.Max(100f, area.rect.width - 24f);
        float cell = height / rows.Count;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; if (row == null) continue;
            var layout = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            Vector2 size = row.rect.size;
            row.anchorMin = row.anchorMax = new Vector2(.5f, 1f);
            row.pivot = new Vector2(.5f, 1f);
            row.sizeDelta = size;
            float scale = Mathf.Min(1f, Mathf.Min(width / Mathf.Max(1f, size.x), (cell - 6f) / Mathf.Max(1f, size.y)));
            row.localScale = Vector3.one * scale;
            row.anchoredPosition = new Vector2(0f, -12f - i * cell);
        }
    }
}
