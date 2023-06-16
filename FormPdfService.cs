using Aspose.Pdf;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Text;
using LargeSample.CustomForms.Models;
using MCConnect.Core.Contracts;
using Newtonsoft.Json.Linq;
using Orchard.Localization.Services;
using Orchard.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LargeSample.CustomForms.Services
{
    public class FormPdfService : IFormPdfService
    {
        private readonly IResourceContentService _resourceContentService;
        private readonly IDateLocalizationServices _dateLocalizerService;

        public ILogger Logger { get; set; } = NullLogger.Instance;

        public FormPdfService(IResourceContentService resourceContentService, IDateLocalizationServices dateLocalizerService)
        {
            _resourceContentService = resourceContentService;
            _dateLocalizerService = dateLocalizerService;
        }

        public IEnumerable<PdfField> ExtractFieldNamesAndTypes(int resourceContentId)
        {
            try
            {
                using (var stream = new MemoryStream(_resourceContentService.GetResourceContent(resourceContentId)))
                using (var document = new Document(stream))
                {
                    var orderedPdfFields = GetOrderedPdfFields(document);

                    LogPotentialErrors(orderedPdfFields);

                    return orderedPdfFields;
                }
            }
            catch (ArgumentNullException ex)
            {
                Logger.Error(ex, "Couldn't find the file resource with ID {0}!", resourceContentId);
                return Enumerable.Empty<PdfField>();
            }
        }

        public byte[] GetAnnotatedPdf(int resourceContentId)
        {
            var pdfContent = _resourceContentService.GetResourceContent(resourceContentId);
            if (pdfContent == null)
            {
                Logger.Error("Couldn't find the ResourceContent with ID {0}!", resourceContentId);
                return Array.Empty<byte>();
            }

            using (var stream = new MemoryStream(pdfContent))
            using (var document = new Document(stream))
            {
                foreach (var field in GetOrderedPdfFields(document))
                {
                    AddLabelToField(field, document.Pages);
                }

                using (var outStream = new MemoryStream())
                {
                    document.Save(outStream, SaveFormat.Pdf);
                    return outStream.ToArray();
                }
            }
        }

        public byte[] FillForm(int resourceContentId, FormFieldMapping formFieldMapping, FormData formData)
        {
            var pdfContent = _resourceContentService.GetResourceContent(resourceContentId);
            if (pdfContent == null)
            {
                Logger.Error("Couldn't find the ResourceContent with ID {0}!", resourceContentId);
                return Array.Empty<byte>();
            }

            var pdfFieldToFormFieldMap = formFieldMapping
                .Normalize()
                .Entries
                .SelectMany(entry => entry.Value.Select(pdfField => Tuple.Create(pdfField, entry.Key)))
                .ToDictionary(pair => pair.Item1, pair => pair.Item2);

            using (var stream = new MemoryStream(pdfContent))
            using (var document = new Document(stream))
            {
                var fieldsWithValues = new List<string>();
                foreach (var pdfField in GetOrderedPdfFields(document))
                {
                    if (!pdfFieldToFormFieldMap.TryGetValue(pdfField.Name, out var formFieldName))
                    {
                        continue;
                    }
                    try
                    {
                        var valueMap = formFieldMapping.ValueMaps.TryGetValue(pdfField.Name, out var map) ? map : null;

                        var formDataObject = formData.FormDataObject;
                        var fieldDataToken = formDataObject.SelectToken(formFieldName);
                        // The value of Date fields should always be localized
                        if (fieldDataToken != null && fieldDataToken.Type == JTokenType.Date)
                        {
                            pdfField.Field.Value =
                                _dateLocalizerService.ConvertToLocalizedDateString(
                                    formData.GetValue<DateTime>(formFieldName));
                        }
                        else
                        {
                            // Set value from other field types
                            pdfField.SetValue(formFieldName, formData, valueMap ?? new Dictionary<string, string>(0));
                        }

                        fieldsWithValues.Add(pdfField.Name);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Setting value failed for \"{pdfField.Name}\".");
                    }
                }

                Logger.Debug($"Setting values successfully for: {fieldsWithValues.Join(", ")}");

                using (var outStream = new MemoryStream())
                {
                    document.Save(outStream, SaveFormat.Pdf);
                    return outStream.ToArray();
                }
            }
        }

        private void LogPotentialErrors(IEnumerable<PdfField> pdfFields)
        {
            var pdfFieldsWithUnknownTypes = pdfFields.Where(pdfField => pdfField.Type == PdfFieldType.Unknown).ToList();
            if (pdfFieldsWithUnknownTypes.Count == 0) return;

            var unknownTypes = pdfFieldsWithUnknownTypes
                .Select(pdfField => pdfField.Field.GetType().Name)
                .Distinct()
                .Join("\", \"");

            Logger.Error(
                $"Support for the following PDF field type(s) has not yet been implemented: \"{unknownTypes}\".");
        }

        private static IList<PdfField> GetOrderedPdfFields(Document document)
        {
            var counter = 0;

            return document.Form.Fields
                .Where(field => !(field is ButtonField))
                // Here we group all RadioButtonOptionFields together with their respective parent.
                .GroupBy(field => field.Parent?.PartialName)
                .SelectMany(group => group.Key == null
                    ? group.Select(field => new PdfField(field))
                    : new[] { new PdfField(group.First().Parent, group) })
                .OrderBy(field => field.PageIndex)
                .ThenByDescending(field => field.TopLeftY)
                .ThenBy(field => field.TopLeftX)
                // Enumerate the fields to simplify the mapping.
                .Select(field => field.SetOrder(++counter))
                .ToList();
        }

        private static void AddLabelToField(PdfField field, PageCollection pages)
        {
            switch (field.Field)
            {
                case CheckboxField checkbox:
                    // Single checkbox.
                    if (checkbox.Count == 0)
                    {
                        AddStampFor(pages[field.PageIndex], field);
                    }

                    // Multiple checkboxes.
                    foreach (var box in checkbox)
                    {
                        AddStampFor(pages[box.PageIndex], box.Rect, field.Order);
                    }

                    break;
                case TextBoxField textBoxField:
                    var order = field.Order.ToInvariantString();
                    var canWriteLongLabel = textBoxField.MaxLen <= 0 || textBoxField.MaxLen - order.Length > 0;

                    // Set font size to 10 here trying to normalize it across the whole document. Still, if text is too
                    // long to fit into a field at the given size, a font size will be used, at which the text will fit.
                    textBoxField.DefaultAppearance.FontSize = 10;

                    // Join order and name with a non-breaking space (\u00A0) to prevent line-wrap inside the field.
                    textBoxField.Value = canWriteLongLabel ? $"({order})\u00A0{field.Name}" : order;
                    break;
                default:
                    // Add a label under the option fields with the name of the Parent container field.
                    AddStampFor(pages[field.PageIndex], field);
                    break;
            }
        }

        private static void AddStampFor(Page page, PdfField field) => AddStampFor(page, field.Field.Rect, field.Order);

        private static void AddStampFor(Page page, Rectangle rectangle, int order)
        {
            var text = $"({order.ToInvariantString()})";
            var stamp = TextStamp(text, rectangle);
            page.AddStamp(stamp);
        }

        private static TextStamp TextStamp(string text, Rectangle rectangle)
        {
            var textState = new TextState
            {
                // #e0eaeb is a lighter version of both #65c8d0 and #229ea8 (used HSL mode).
                BackgroundColor = Color.FromArgb(0xe0, 0xea, 0xeb),
                ForegroundColor = Color.DarkRed,
            };
            var stamp = new TextStamp(text, textState)
            {
                XIndent = rectangle.LLX,

                // Move the stamp down a bit from its Checkbox, so as to not hide it completely.
                YIndent = rectangle.LLY - 6,

                // Make enough room for the labels to be readable.
                Width = text.Length * 4,

                Height = 12,
            };
            return stamp;
        }
    }
}
