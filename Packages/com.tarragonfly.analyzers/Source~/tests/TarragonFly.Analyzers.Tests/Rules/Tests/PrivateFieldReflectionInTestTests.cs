using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Tests;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Tests
{
    // TF9008 reflection into a private field in a test.
    public class PrivateFieldReflectionInTestTests
    {
        private const string Code = @"
using System;
using System.Reflection;
using NUnit.Framework;

class Board
{
    int _rows;
    public int Columns;
}

class BoardTests
{
    [Test]
    public void Sets()
    {
        Board board = new Board();
        FieldInfo rows = typeof(Board).{|TF9008:GetField|}(""_rows"", BindingFlags.NonPublic | BindingFlags.Instance);
        rows.{|TF9008:SetValue|}(board, 8);
        int value = (int)rows.{|TF9008:GetValue|}(board);
        typeof(Board).{|TF9008:GetField|}(""_rows"", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(board, 9);
        board.GetType().{|TF9008:GetField|}(""_rows"", BindingFlags.NonPublic).SetValue(board, 9);
        Type type = typeof(Board);
        type.{|TF9008:GetField|}(""_rows"", BindingFlags.Default | BindingFlags.NonPublic);

        FieldInfo columns = typeof(Board).GetField(""Columns"");
        typeof(Board).GetField(""Columns"", BindingFlags.Public | BindingFlags.Instance);
        columns.{|TF9008:SetValue|}(board, 3);
        PropertyInfo count = typeof(Board).GetProperty(""Count"", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(value, Is.EqualTo(8));
    }
}

static class BoardFakes
{
    public static void Set(Board board) => typeof(Board).GetField(""_rows"", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(board, 8);

    class Poker
    {
        void Poke(Board board) => typeof(Board).GetField(""_rows"", BindingFlags.NonPublic).SetValue(board, 1);
    }
}";

        [Test]
        public void ReflectionIntoAPrivateField_IsReportedOutsideFakes()
        {
            AnalyzerVerifier.Verify(new PrivateFieldReflectionInTestAnalyzer(), Code, SnippetAssembly.Tests());
        }

        [Test]
        public void OutsideTestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new PrivateFieldReflectionInTestAnalyzer(), Markup.Without(Code, "TF9008"), new SnippetAssembly { EditorOnly = true });
        }

        [Test]
        public void TestSupportAssembly_IsNotChecked()
        {
            SnippetAssembly shared = SnippetAssembly.Tests("test_support_assemblies = *.Tests.Shared");
            shared.AssemblyName = "Game.Tests.Shared";
            AnalyzerVerifier.Verify(new PrivateFieldReflectionInTestAnalyzer(), Markup.Without(Code, "TF9008"), shared);
            AnalyzerVerifier.Verify(new PrivateFieldReflectionInTestAnalyzer(), Code, SnippetAssembly.Tests("test_support_assemblies = *.Tests.Shared"));
        }
    }
}
