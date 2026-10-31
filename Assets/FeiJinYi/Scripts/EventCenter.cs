// 泛型事件中心
// 事件类型使用枚举，避免事件名拼写错误。

using System;
using System.Collections.Generic;

public class EventCenter<TEvent> where TEvent : struct, Enum
{
    public static EventCenter<TEvent> Instance { get; } = new EventCenter<TEvent>();

    private readonly Dictionary<TEvent, HashSet<Action>> listeners =
        new Dictionary<TEvent, HashSet<Action>>();

    private EventCenter()
    {
    }

    /// <summary>
    /// 注册一个事件订阅者。
    /// </summary>
    public void AddListener(TEvent eventName, Action listener)
    {
        if (listener == null)
        {
            throw new ArgumentNullException(nameof(listener));
        }

        if (!listeners.TryGetValue(eventName, out var eventListeners))
        {
            eventListeners = new HashSet<Action>();
            listeners[eventName] = eventListeners;
        }

        eventListeners.Add(listener);
    }

    /// <summary>
    /// 取消注册一个事件订阅者。
    /// </summary>
    public void RemoveListener(TEvent eventName, Action listener)
    {
        if (listeners.TryGetValue(eventName, out var eventListeners))
        {
            eventListeners.Remove(listener);

            if (eventListeners.Count == 0)
            {
                listeners.Remove(eventName);
            }
        }
    }

    /// <summary>
    /// 触发指定事件。所有已注册的订阅者都会执行。
    /// </summary>
    public void Dispatch(TEvent eventName)
    {
        if (!listeners.TryGetValue(eventName, out var eventListeners))
        {
            return;
        }

        // 复制订阅者列表，避免订阅者在触发时添加或删除监听器导致异常。
        var listenersToInvoke = new List<Action>(eventListeners);
        foreach (var listener in listenersToInvoke)
        {
            listener?.Invoke();
        }
    }

    /// <summary>
    /// 清空指定事件的所有订阅者。
    /// </summary>
    public void ClearListeners(TEvent eventName)
    {
        listeners.Remove(eventName);
    }

    /// <summary>
    /// 清空所有事件的订阅者。
    /// </summary>
    public void ClearAllListeners()
    {
        listeners.Clear();
    }
}

