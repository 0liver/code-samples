using LargeSample.CustomForms.Measures;
using LargeSample.CustomForms.Models;
using LargeSample.CustomForms.Services;
using LargeSample.CustomForms.Utilities;
using LargeSample.Observations.ResourceModels;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Orchard;
using Orchard.Settings;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LargeSample.CustomForms.Tests
{
    public class FormSchemaTests
    {
        [Fact]
        public void FormSchemaSerializationRoundTripShouldReturnSameCustomClass()
        {
            var formSchema = new
            {
                Components = new[] { new FormComponentSchema().AddCssClasses("one") },
            };
            var formSchemaJson = JsonConvert.SerializeObject(formSchema, FormSchemaService.JsonSerializationSettings);
            var roundtripped = new FormSchema(formSchemaJson);

            ((string)roundtripped.GetAllComponents().First()["customClass"]).ShouldBe("one");
        }

        [Fact]
        public void FormSchemaModificationShouldRegenerateOriginalJson()
        {
            const string compactFormSchemaJson = @"{""components"":[{""type"":""signature""}]}";
            var formSchemaService = new FormSchemaService(
                Array.Empty<IFormSchemaDisplayModifier>(), formSchemaEditModifiers: null);
            var currentJson = formSchemaService.GetCurrentToDisplay(new FormSchema(compactFormSchemaJson)).ToString();

            currentJson.ShouldBe(compactFormSchemaJson);
        }

        [Fact]
        public void FormSchemaModificationShouldReplaceComponentsInPlace()
        {
            var formSchema = new
            {
                Components = new[] { new FormComponentSchema().SetType(FieldType.Textfield) },
            };
            var formSchemaJson = JsonConvert.SerializeObject(formSchema, FormSchemaService.JsonSerializationSettings);
            var formSchemaService = new FormSchemaService(
                new[] { new TestSchemaModifier(@"{ ""key"": ""newKey"" }") }, formSchemaEditModifiers: null);
            var currentSchemaJson = formSchemaService.GetCurrentToDisplay(new FormSchema(formSchemaJson)).ToString();

            currentSchemaJson.ShouldContain("newKey");
            currentSchemaJson.ShouldNotContain("signature");
        }

        [Fact]
        public void FormSchemaShouldReturnDeeplyNestedMeasureNames()
        {
            MeasureDefinition GetMeasureDefinition(string name) => new MeasureDefinition
            {
                Name = name,
                SubMeasures = new List<SubMeasureDefinition>(),
            };

            var formSchema = new
            {
                Components = new[]
                {
                    new MeasureFieldComponentSchema(GetMeasureDefinition("First")),
                    new FormComponentSchema().SetContainer(
                        new MeasureFieldComponentSchema(GetMeasureDefinition("Second")),
                        new FormComponentSchema().SetContainer(
                            new MeasureFieldComponentSchema(GetMeasureDefinition("Sixth")))),
                },
            };
            var formSchemaJson = JsonConvert.SerializeObject(formSchema, FormSchemaService.JsonSerializationSettings);

            new FormSchema(formSchemaJson).GetMeasureNames().ShouldBe(new[]
            {
                "First",
                "Second",
                "Sixth",
            });
        }

        [Fact]
        public void FormSchemaShouldReturnStandardFieldKeysInColumns()
        {
            const string formSchemaJson = /*lang=json,strict*/ @"
{
  ""label"": ""Columns"",
  ""columns"": [
    {
      ""components"": [
        {
          ""key"": ""panel"",
          ""components"": [
            {
              ""label"": ""First Name"",
              ""key"": ""StandardField__Client__FirstName"",
              ""properties"": {
                ""persistenceMode"": ""updateOnServer""
              }
            }
          ]
        }
      ]
    }
  ],
  ""components"": [
    {
      ""label"": ""Surname"",
      ""key"": ""StandardField__Client__Surname"",
      ""properties"": {
        ""persistenceMode"": ""updateOnServer""
      }
    }
  ]
}";
            new FormSchema(formSchemaJson)
                .GetStandardFieldKeysToUpdate()
                .ShouldBe(
                    new[] { "StandardField__Client__FirstName", "StandardField__Client__Surname" }, ignoreOrder: true);
        }

        [Fact]
        public void FormComponentSchemaJsonShouldContainAdditionalProperties()
        {
            var schema = new FormComponentSchema().SetLabel("LABEL").SetProperty("other", "value");
            var wrapper = new FormComponentSchemaSerializationWrapper(schema);

            var json = JsonConvert.SerializeObject(wrapper, FormSchemaService.JsonSerializationSettings);
            var deserialized = JsonConvert.DeserializeObject<dynamic>(json);
            ((string)deserialized.other).ShouldBe("value");
        }

        [Fact]
        public void FormComponentSchemaChildrenJsonShouldContainAdditionalProperties()
        {
            var child = new FormComponentSchema().SetLabel("LABEL").SetProperty("other", "value");
            var schema = new FormComponentSchema().SetContainer(child);
            var wrapper = new FormComponentSchemaSerializationWrapper(schema);

            var json = JsonConvert.SerializeObject(wrapper, FormSchemaService.JsonSerializationSettings);
            var deserialized = JsonConvert.DeserializeObject<dynamic>(json);
            ((string)deserialized.components[0].other).ShouldBe("value");
        }

        [Fact]
        public void FormComponentSchemaAdditionalPropertiesShouldOverrideDefinedProperties()
        {
            var schema =
                new FormComponentSchema().SetLabel("LABEL").SetProperty("label", "OTHER").SetProperty("a", 1);
            var wrapper = new FormComponentSchemaSerializationWrapper(schema);

            var json = JsonConvert.SerializeObject(wrapper, FormSchemaService.JsonSerializationSettings);
            var deserialized = JsonConvert.DeserializeObject<dynamic>(json);

            ((string)deserialized.Label).ShouldBe(expected: null);
            ((string)deserialized.label).ShouldBe("OTHER");
            ((int)deserialized.a).ShouldBe(1);
        }

        [Fact]
        public void FormComponentSchemaJsonPropertyShouldExtendDefinedProperty()
        {
            var schema = new FormComponentSchema().SetRequired().SetPropertyJson("validate", "{ maxlength: 50 }");
            var wrapper = new FormComponentSchemaSerializationWrapper(schema);

            var json = JsonConvert.SerializeObject(wrapper, FormSchemaService.JsonSerializationSettings);
            var deserialized = JsonConvert.DeserializeObject<dynamic>(json);

            ((bool)deserialized.validate.required).ShouldBe(expected: true);
            ((int)deserialized.validate.maxlength).ShouldBe(50);
        }

        [Fact]
        public void FormComponentSchemaOverridesShouldExtendSchema()
        {
            var schema = new FormComponentSchema().SetRequired();
            schema.MergeJson("{ validate: { maxlength: 50 }, other: \"hello\" }");
            var wrapper = new FormComponentSchemaSerializationWrapper(schema);

            var json = JsonConvert.SerializeObject(wrapper, FormSchemaService.JsonSerializationSettings);
            var deserialized = JsonConvert.DeserializeObject<dynamic>(json);

            ((bool)deserialized.validate.required).ShouldBe(expected: true);
            ((int)deserialized.validate.maxlength).ShouldBe(50);
            ((string)deserialized.other).ShouldBe("hello");
        }

        [Fact]
        public void FormSchemaShouldChangeUrl()
        {
            const string formSchemaJson = @"
{
  ""components"": [
    {
      ""components"": [
        {
            ""data"":
            {
                ""url"":""/test/relative"",
            },
        }, 
        {
            ""data"":
            {
                ""url"":""../test/relative"",
            },
        }
      ]
    }
  ]
}";

            var site = new Mock<ISite>();
            site.Setup(x => x.BaseUrl).Returns("localhost");
            var schemaModifier = new UrlFormSchemaModifier(new TestWorkContext(site.Object));
            var formSchemaService = new FormSchemaService(new[] { schemaModifier }, formSchemaEditModifiers: null);

            var updatedComponents = formSchemaService
                .GetCurrentToDisplay(new FormSchema(formSchemaJson))
                .GetAllComponents()
                .Where(jToken => jToken["data"] != null);
            var changedComponent = updatedComponents.First();
            var unchangedComponent = updatedComponents.Skip(1).First();

            changedComponent["data"]["url"].Value<string>().ShouldBe("localhost/test/relative");
            unchangedComponent["data"]["url"].Value<string>().ShouldBe("../test/relative");
        }

        public class TestSchemaModifier : IFormSchemaDisplayModifier, IFormSchemaEditModifier
        {
            private readonly string _replacementJson;

            public TestSchemaModifier(string replacementJson) => _replacementJson = replacementJson;

            public FormSchema Modify(FormSchema formSchema)
            {
                foreach (var component in formSchema.GetAllComponents())
                {
                    Console.WriteLine(component);
                    component.Replace(JToken.Parse(_replacementJson));
                }

                return formSchema;
            }
        }

        public class TestWorkContext : WorkContext
        {
            private readonly IDictionary<string, object> _state = new Dictionary<string, object>();

            public TestWorkContext(ISite site) => CurrentSite = site;

            public override T GetState<T>(string name) => (T)_state[name];
            public override T Resolve<T>() => throw new NotSupportedException();
            public override object Resolve(Type serviceType) => throw new NotSupportedException();
            public override void SetState<T>(string name, T value) => _state.Add(name, value);
            public override bool TryResolve<T>(out T service) => throw new NotSupportedException();
            public override bool TryResolve(Type serviceType, out object service) => throw new NotSupportedException();
        }
    }
}
