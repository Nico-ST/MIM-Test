// -----------------------------------------------------------------------------
// Author : N.Stillhart
// Date   : 2026-09-28
// Purpose: Unit tests for InheritanceCalculator (configuration validation and
//          change calculation between main identity and sub-identity).
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using MIM.ActorInheritance.Workflow;
using Xunit;

namespace MIM.ActorInheritance.Workflow.Tests
{
    public class InheritanceCalculatorTests
    {
        private static readonly Guid ActorA = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid ActorB = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid ActorC = new Guid("33333333-3333-3333-3333-333333333333");

        [Fact]
        public void ParseAttributeList_SplitsOnSeparatorsAndRemovesDuplicates()
        {
            IList<string> attributes = InheritanceCalculator.ParseAttributeList(" Actor;Manager, CostCenter\r\nActor\tDepartment ");

            Assert.Equal(new[] { "Actor", "Manager", "CostCenter", "Department" }, attributes);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ; ,\n")]
        public void ParseAttributeList_ReturnsEmptyListForBlankInput(string value)
        {
            Assert.Empty(InheritanceCalculator.ParseAttributeList(value));
        }

        [Fact]
        public void ValidateConfiguration_AcceptsValidConfiguration()
        {
            Assert.Empty(InheritanceCalculator.ValidateConfiguration("Person", "MainIdentity", new[] { "Actor", "Manager" }));
        }

        [Theory]
        [InlineData("Person']", "MainIdentity", "Actor")]
        [InlineData("Person", "Main Identity", "Actor")]
        [InlineData("Person", "MainIdentity", "Actor' or '1'='1")]
        [InlineData("Person", "MainIdentity", "1Actor")]
        public void ValidateConfiguration_RejectsInvalidNames(string objectType, string mainIdentityAttribute, string inheritedAttribute)
        {
            Assert.NotEmpty(InheritanceCalculator.ValidateConfiguration(objectType, mainIdentityAttribute, new[] { inheritedAttribute }));
        }

        [Fact]
        public void ValidateConfiguration_RequiresAtLeastOneInheritedAttribute()
        {
            Assert.NotEmpty(InheritanceCalculator.ValidateConfiguration("Person", "MainIdentity", new string[0]));
            Assert.NotEmpty(InheritanceCalculator.ValidateConfiguration("Person", "MainIdentity", null));
        }

        [Theory]
        [InlineData("MainIdentity")]
        [InlineData("ObjectID")]
        [InlineData("objecttype")]
        [InlineData("CreatedTime")]
        public void ValidateConfiguration_RejectsMainIdentityAndSystemAttributes(string inheritedAttribute)
        {
            Assert.NotEmpty(InheritanceCalculator.ValidateConfiguration("Person", "MainIdentity", new[] { inheritedAttribute }));
        }

        [Fact]
        public void CalculateChanges_ReturnsNothingWhenValuesAreEqual()
        {
            IList<AttributeChange> changes = Calculate(
                new[] { "Actor", "Department", "Roles" },
                Values(("Actor", ActorA), ("Department", "IT"), ("Roles", Multi("A", "B"))),
                Values(("Actor", ActorA), ("Department", "IT"), ("Roles", Multi("B", "A"))));

            Assert.Empty(changes);
        }

        [Fact]
        public void CalculateChanges_ModifiesDifferentSingleValue()
        {
            AttributeChange change = Assert.Single(Calculate(new[] { "Actor" }, Values(("Actor", ActorA)), Values(("Actor", ActorB))));

            AssertChange(change, "Actor", AttributeChangeMode.Modify, ActorA);
        }

        [Fact]
        public void CalculateChanges_SetsSingleValueMissingOnSubIdentity()
        {
            AttributeChange change = Assert.Single(Calculate(new[] { "Actor" }, Values(("Actor", ActorA)), Values()));

            AssertChange(change, "Actor", AttributeChangeMode.Modify, ActorA);
        }

        [Fact]
        public void CalculateChanges_RemovesSingleValueClearedOnMainIdentity()
        {
            AttributeChange change = Assert.Single(Calculate(new[] { "Actor" }, Values(("Actor", null)), Values(("Actor", ActorB))));

            AssertChange(change, "Actor", AttributeChangeMode.Remove, ActorB);
        }

        [Fact]
        public void CalculateChanges_TreatsEmptyStringAsNoValue()
        {
            Assert.Empty(Calculate(new[] { "Department" }, Values(("Department", string.Empty)), Values(("Department", null))));

            AttributeChange change = Assert.Single(Calculate(new[] { "Department" }, Values(("Department", string.Empty)), Values(("Department", "IT"))));
            AssertChange(change, "Department", AttributeChangeMode.Remove, "IT");
        }

        [Fact]
        public void CalculateChanges_ComparesStringsCaseSensitive()
        {
            AttributeChange change = Assert.Single(Calculate(new[] { "Department" }, Values(("Department", "IT")), Values(("Department", "it"))));

            AssertChange(change, "Department", AttributeChangeMode.Modify, "IT");
        }

        [Fact]
        public void CalculateChanges_AddsAndRemovesMultiValues()
        {
            IList<AttributeChange> changes = Calculate(
                new[] { "Actors" },
                Values(("Actors", Multi(ActorA, ActorB))),
                Values(("Actors", Multi(ActorB, ActorC))));

            Assert.Equal(2, changes.Count);
            AssertChange(changes.Single(c => c.Mode == AttributeChangeMode.Remove), "Actors", AttributeChangeMode.Remove, ActorC);
            AssertChange(changes.Single(c => c.Mode == AttributeChangeMode.Insert), "Actors", AttributeChangeMode.Insert, ActorA);
        }

        [Fact]
        public void CalculateChanges_InsertsAllValuesWhenSubIdentityHasNone()
        {
            IList<AttributeChange> changes = Calculate(new[] { "Actors" }, Values(("Actors", Multi(ActorA, ActorB, ActorA))), Values());

            Assert.Equal(new object[] { ActorA, ActorB }, changes.Select(c => c.Value));
            Assert.All(changes, c => Assert.Equal(AttributeChangeMode.Insert, c.Mode));
        }

        [Fact]
        public void CalculateChanges_RemovesAllValuesWhenMainIdentityHasNone()
        {
            IList<AttributeChange> changes = Calculate(new[] { "Actors" }, Values(("Actors", Multi())), Values(("Actors", Multi(ActorA, ActorB))));

            Assert.Equal(new object[] { ActorA, ActorB }, changes.Select(c => c.Value));
            Assert.All(changes, c => Assert.Equal(AttributeChangeMode.Remove, c.Mode));
        }

        [Fact]
        public void CalculateChanges_ComparesBinaryValuesByContent()
        {
            Assert.Empty(Calculate(new[] { "Photo" }, Values(("Photo", new byte[] { 1, 2, 3 })), Values(("Photo", new byte[] { 1, 2, 3 }))));
            Assert.Single(Calculate(new[] { "Photo" }, Values(("Photo", new byte[] { 1, 2, 3 })), Values(("Photo", new byte[] { 1, 2 }))));
        }

        [Fact]
        public void CalculateChanges_ComparesIntegersAcrossNumericTypes()
        {
            Assert.Empty(Calculate(new[] { "Level" }, Values(("Level", 5)), Values(("Level", 5L))));
        }

        [Fact]
        public void CalculateChanges_IgnoresAttributesThatAreNotInherited()
        {
            Assert.Empty(Calculate(new[] { "Actor" }, Values(("Actor", ActorA), ("Email", "a@example.com")), Values(("Actor", ActorA))));
        }

        [Fact]
        public void CalculateChanges_HandlesMissingValueDictionaries()
        {
            AttributeChange change = Assert.Single(InheritanceCalculator.CalculateChanges(new[] { "Actor" }, Values(("Actor", ActorA)), null));
            AssertChange(change, "Actor", AttributeChangeMode.Modify, ActorA);

            Assert.Empty(InheritanceCalculator.CalculateChanges(new[] { "Actor" }, null, null));
        }

        [Fact]
        public void PendingUpdate_CopiesChangesAndRequiresResourceId()
        {
            List<AttributeChange> changes = new List<AttributeChange> { new AttributeChange("Actor", AttributeChangeMode.Modify, ActorA) };
            PendingUpdate update = new PendingUpdate(ActorB, changes);
            changes.Clear();

            Assert.Single(update.Changes);
            Assert.Throws<ArgumentException>(() => new PendingUpdate(Guid.Empty, changes));
            Assert.Throws<ArgumentNullException>(() => new AttributeChange("Actor", AttributeChangeMode.Modify, null));
        }

        private static IList<AttributeChange> Calculate(string[] attributes, Dictionary<string, object> desired, Dictionary<string, object> current)
        {
            return InheritanceCalculator.CalculateChanges(attributes, desired, current);
        }

        private static Dictionary<string, object> Values(params (string Name, object Value)[] values)
        {
            return values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        }

        private static List<object> Multi(params object[] values)
        {
            return new List<object>(values);
        }

        private static void AssertChange(AttributeChange change, string attributeName, AttributeChangeMode mode, object value)
        {
            Assert.Equal(attributeName, change.AttributeName);
            Assert.Equal(mode, change.Mode);
            Assert.Equal(value, change.Value);
        }
    }
}
