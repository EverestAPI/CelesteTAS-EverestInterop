using System;

namespace CelesteStudio.RichText {
public class LimitedStack<T> {
    int count;
    T[] items;
    int start;

    public LimitedStack(int maxItemCount) {
        items = new T[maxItemCount];
        count = 0;
        start = 0;
    }

    public int MaxItemCount {
        get { return items.Length; }
    }

    public int Count {
        get { return count; }
    }

    int LastIndex {
        get { return (start + count - 1) % items.Length; }
    }

    public T Pop() {
        if (count == 0) {
            throw new Exception("Stack is empty");
        }

        int i = LastIndex;
        T item = items[i];
        items[i] = default(T);

        count--;

        return item;
    }

    public T Peek() {
        if (count == 0) {
            return default(T);
        }

        return items[LastIndex];
    }

    public void Push(T item) {
        if (count == items.Length) {
            start = (start + 1) % items.Length;
        } else {
            count++;
        }

        items[LastIndex] = item;
    }

    public void Clear() {
        items = new T[items.Length];
        count = 0;
        start = 0;
    }

    public T[] ToArray() {
        T[] result = new T[count];
        for (int i = 0; i < count; i++) {
            result[i] = items[(start + i) % items.Length];
        }

        return result;
    }
}
}