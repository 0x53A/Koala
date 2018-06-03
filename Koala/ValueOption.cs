using System;
using System.Collections.Generic;
using System.Text;

namespace Koala
{
    public class ValueOption
    {
        private ValueOption() { }

        public static ValueOption<T> Some<T>(T v) => new ValueOption<T>(v);
        public static ValueOption None { get; } = new ValueOption();
    }

    public struct ValueOption<T>
    {
        public static ValueOption<T> None => new ValueOption<T>();

        public T Value { get; }
        public bool IsSome { get; }
        public bool IsNone => !IsSome;

        public ValueOption(T val)
        {
            Value = val;
            IsSome = true;
        }

        public static implicit operator ValueOption<T>(ValueOption option) => ValueOption<T>.None;

        public TResult Match<TResult>(Func<T, TResult> onSome, Func<TResult> onNone)
        {
            if (this.IsSome)
                return onSome(Value);
            else
                return onNone();
        }

        public TResult Match<TResult>(Func<T, TResult> onSome, TResult onNone)
        {
            if (this.IsSome)
                return onSome(Value);
            else
                return onNone;
        }
    }
}
