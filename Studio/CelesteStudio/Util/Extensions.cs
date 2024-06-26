using System;
using System.Linq;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;

namespace CelesteStudio.Util;

public static class Extensions
{
    public static string[] SplitDocumentLines(this string self, StringSplitOptions options = StringSplitOptions.None) => self.Split(Document.NewLine, options);

    public static int Digits(this int self) => Math.Abs(self).ToString().Length;
    
    public static string ReplaceRange(this string self, int startIndex, int count, string replacement) => self.Remove(startIndex, count).Insert(startIndex, replacement);
    
    public static CommonControl WithFontStyle(this CommonControl self, FontStyle style) {
        self.Font = new Font(self.Font.Family, self.Font.Size, style);
        return self;
    }
    
    public static int IndexOf<T>(this IEnumerable<T> obj, T value) => obj.IndexOf(value, EqualityComparer<T>.Default);
    public static int IndexOf<T>(this IEnumerable<T> obj, T value, IEqualityComparer<T> comparer) {
        using var iter = obj.GetEnumerator();
        
        int i = 0;
        while (iter.MoveNext()) {
            if (comparer.Equals(iter.Current, value))
                return i;
            i++;
        }
        
        return -1;
    }
    
    // Stolen from https://stackoverflow.com/a/36845864
    public static int GetStableHashCode(this string str)
    {
        unchecked
        {
            int hash1 = 5381;
            int hash2 = hash1;
            
            for(int i = 0; i < str.Length && str[i] != '\0'; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ str[i];
                if (i == str.Length - 1 || str[i+1] == '\0')
                    break;
                hash2 = ((hash2 << 5) + hash2) ^ str[i+1];
            }
            
            return hash1 + (hash2*1566083941);
        }
    }
}