using System;
using System.Collections.Generic;

namespace Satisfying.Tests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string filter = args.Length > 0 ? args[0] : null;
            return TestRunner.RunAll(filter);
        }
    }

    public static class Assert
    {
        public static void True(bool cond, string message)
        {
            if (!cond) throw new Exception("Expected true: " + message);
        }

        public static void False(bool cond, string message)
        {
            if (cond) throw new Exception("Expected false: " + message);
        }

        public static void Near(float actual, float expected, float tolerance, string message)
        {
            if (Math.Abs(actual - expected) > tolerance)
                throw new Exception(string.Format("{0}: expected {1:0.####} +/- {2:0.####}, got {3:0.####}", message, expected, tolerance, actual));
        }

        public static void Less(float actual, float limit, string message)
        {
            if (!(actual < limit))
                throw new Exception(string.Format("{0}: expected < {1:0.####}, got {2:0.####}", message, limit, actual));
        }

        public static void Greater(float actual, float limit, string message)
        {
            if (!(actual > limit))
                throw new Exception(string.Format("{0}: expected > {1:0.####}, got {2:0.####}", message, limit, actual));
        }

        public static void Equal(int actual, int expected, string message)
        {
            if (actual != expected)
                throw new Exception(string.Format("{0}: expected {1}, got {2}", message, expected, actual));
        }
    }

    public sealed class TestCase
    {
        public string Name;
        public Action Body;
    }

    public static class TestRunner
    {
        static readonly List<TestCase> Cases = new List<TestCase>();

        public static void Add(string name, Action body)
        {
            Cases.Add(new TestCase { Name = name, Body = body });
        }

        public static int RunAll(string filter)
        {
            Cases.Clear();
            MovementTests.Register();
            LeanTests.Register();
            SerializationTests.Register();
            NetIntegrationTests.Register();
            CombatTests.Register();
            TraversalTests.Register();
            MeleeTests.Register();
            PropTests.Register();
            PortMapperTests.Register();
            UdpTransportTests.Register();
            NetAddressTests.Register();
            RealSocketTests.Register();
            TouchRigTests.Register();
            ReachabilityTests.Register();

            int passed = 0;
            List<string> failures = new List<string>();
            foreach (TestCase c in Cases)
            {
                if (filter != null && c.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                try
                {
                    c.Body();
                    passed++;
                    Console.WriteLine("  PASS  " + c.Name);
                }
                catch (Exception e)
                {
                    failures.Add(c.Name + " -> " + e.Message);
                    Console.WriteLine("  FAIL  " + c.Name + "\n        " + e.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine(string.Format("{0} passed, {1} failed", passed, failures.Count));
            return failures.Count == 0 ? 0 : 1;
        }
    }
}
