// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Components.RenderTree;

namespace Microsoft.AspNetCore.Components.Web
{
    internal class WebEventData
    {
        // This class represents the second half of parsing incoming event data,
        // once the event ID (and possibly the type of the eventArgs) becomes known.
        public static WebEventData Parse(
            Renderer renderer,
            WebEventJsonContext jsonSerializerContext,
            string eventDescriptorJson,
            string eventArgsJson)
        {
            WebEventDescriptor eventDescriptor;
            try
            {
                eventDescriptor = Deserialize(eventDescriptorJson, jsonSerializerContext.WebEventDescriptor);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("Error parsing the event descriptor", e);
            }

            return Parse(
                renderer,
                jsonSerializerContext,
                eventDescriptor,
                eventArgsJson);
        }

        public static WebEventData Parse(
            Renderer renderer,
            WebEventJsonContext jsonSerializerContext,
            WebEventDescriptor eventDescriptor,
            string eventArgsJson)
        {
            var parsedEventArgs = ParseEventArgsJson(renderer, jsonSerializerContext, eventDescriptor.EventHandlerId, eventDescriptor.EventName, eventArgsJson);
            return new WebEventData(
                eventDescriptor.BrowserRendererId,
                eventDescriptor.EventHandlerId,
                InterpretEventFieldInfo(eventDescriptor.EventFieldInfo),
                parsedEventArgs);
        }

        private WebEventData(int browserRendererId, ulong eventHandlerId, EventFieldInfo? eventFieldInfo, EventArgs eventArgs)
        {
            BrowserRendererId = browserRendererId;
            EventHandlerId = eventHandlerId;
            EventFieldInfo = eventFieldInfo;
            EventArgs = eventArgs;
        }

        public int BrowserRendererId { get; }

        public ulong EventHandlerId { get; }

        public EventFieldInfo? EventFieldInfo { get; }

        public EventArgs EventArgs { get; }

        private static EventArgs ParseEventArgsJson(
            Renderer renderer,
            WebEventJsonContext jsonSerializerContext,
            ulong eventHandlerId,
            string eventName,
            string eventArgsJson)
        {
            try
            {
                if (TryDeserializeStandardWebEventArgs(eventName, eventArgsJson, jsonSerializerContext, out var eventArgs))
                {
                    return eventArgs;
                }

                // For custom events, the args type is determined from the associated delegate
                var eventArgsType = renderer.GetEventArgsType(eventHandlerId);
                return (EventArgs)JsonSerializer.Deserialize(eventArgsJson, eventArgsType, jsonSerializerContext.Options)!;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"There was an error parsing the event arguments. EventId: '{eventHandlerId}'.", e);
            }
        }

        private static bool TryDeserializeStandardWebEventArgs(
            string eventName,
            string eventArgsJson,
            WebEventJsonContext jsonSerializerContext,
            [NotNullWhen(true)] out EventArgs? eventArgs)
        {
            // For back-compatibility, we recognize the built-in list of web event names and hard-code
            // rules about the deserialization type for their eventargs. This makes it possible to declare
            // an event handler as receiving EventArgs, and have it actually receive a subclass at runtime
            // depending on the event that was raised.
            //
            // The following list should remain in sync with EventArgsFactory.ts.

            switch (eventName)
            {
                case "input":
                case "change":
                    // Special case for ChangeEventArgs because its value type can be one of
                    // several types, and System.Text.Json doesn't pick types dynamically
                    eventArgs = DeserializeChangeEventArgs(eventArgsJson, jsonSerializerContext);
                    return true;

                case "copy":
                case "cut":
                case "paste":
                    eventArgs = Deserialize<ClipboardEventArgs>(eventArgsJson, jsonSerializerContext.ClipboardEventArgs);
                    return true;

                case "drag":
                case "dragend":
                case "dragenter":
                case "dragleave":
                case "dragover":
                case "dragstart":
                case "drop":
                    eventArgs = Deserialize<DragEventArgs>(eventArgsJson, jsonSerializerContext.DragEventArgs);
                    return true;

                case "focus":
                case "blur":
                case "focusin":
                case "focusout":
                    eventArgs = Deserialize<FocusEventArgs>(eventArgsJson, jsonSerializerContext.FocusEventArgs);
                    return true;

                case "keydown":
                case "keyup":
                case "keypress":
                    eventArgs = Deserialize<KeyboardEventArgs>(eventArgsJson, jsonSerializerContext.KeyboardEventArgs);
                    return true;

                case "contextmenu":
                case "click":
                case "mouseover":
                case "mouseout":
                case "mousemove":
                case "mousedown":
                case "mouseup":
                case "dblclick":
                    eventArgs = Deserialize<MouseEventArgs>(eventArgsJson, jsonSerializerContext.MouseEventArgs);
                    return true;

                case "error":
                    eventArgs = Deserialize<ErrorEventArgs>(eventArgsJson, jsonSerializerContext.ErrorEventArgs);
                    return true;

                case "loadstart":
                case "timeout":
                case "abort":
                case "load":
                case "loadend":
                case "progress":
                    eventArgs = Deserialize<ProgressEventArgs>(eventArgsJson, jsonSerializerContext.ProgressEventArgs);
                    return true;

                case "touchcancel":
                case "touchend":
                case "touchmove":
                case "touchenter":
                case "touchleave":
                case "touchstart":
                    eventArgs = Deserialize<TouchEventArgs>(eventArgsJson, jsonSerializerContext.TouchEventArgs);
                    return true;

                case "gotpointercapture":
                case "lostpointercapture":
                case "pointercancel":
                case "pointerdown":
                case "pointerenter":
                case "pointerleave":
                case "pointermove":
                case "pointerout":
                case "pointerover":
                case "pointerup":
                    eventArgs = Deserialize<PointerEventArgs>(eventArgsJson, jsonSerializerContext.PointerEventArgs);
                    return true;

                case "wheel":
                case "mousewheel":
                    eventArgs = Deserialize<WheelEventArgs>(eventArgsJson, jsonSerializerContext.WheelEventArgs);
                    return true;

                case "toggle":
                    eventArgs = Deserialize<EventArgs>(eventArgsJson, jsonSerializerContext.EventArgs);
                    return true;

                default:
                    // For custom event types, there are no built-in rules, so the deserialization type is
                    // determined by the parameter declared on the delegate.
                    eventArgs = null;
                    return false;
            }
        }

        private static EventFieldInfo? InterpretEventFieldInfo(EventFieldInfo? fieldInfo)
        {
            // The incoming field value can be either a bool or a string, but since the .NET property
            // type is 'object', it will deserialize initially as a JsonElement
            if (fieldInfo?.FieldValue is JsonElement attributeValueJsonElement)
            {
                switch (attributeValueJsonElement.ValueKind)
                {
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        return new EventFieldInfo
                        {
                            ComponentId = fieldInfo.ComponentId,
                            FieldValue = attributeValueJsonElement.GetBoolean()
                        };
                    default:
                        return new EventFieldInfo
                        {
                            ComponentId = fieldInfo.ComponentId,
                            FieldValue = attributeValueJsonElement.GetString()!
                        };
                }
            }

            return null;
        }

        static T Deserialize<T>(string json, JsonTypeInfo<T?> jsonTypeInfo) => JsonSerializer.Deserialize(json, jsonTypeInfo)!;

        private static ChangeEventArgs DeserializeChangeEventArgs(string eventArgsJson, WebEventJsonContext jsonSerializerContext)
        {
            var changeArgs = Deserialize(eventArgsJson, jsonSerializerContext.ChangeEventArgs);
            var jsonElement = (JsonElement)changeArgs.Value!;
            switch (jsonElement.ValueKind)
            {
                case JsonValueKind.Null:
                    changeArgs.Value = null;
                    break;
                case JsonValueKind.String:
                    changeArgs.Value = jsonElement.GetString();
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    changeArgs.Value = jsonElement.GetBoolean();
                    break;
                default:
                    throw new ArgumentException($"Unsupported {nameof(ChangeEventArgs)} value {jsonElement}.");
            }
            return changeArgs;
        }
    }

    [JsonSerializable(typeof(WebEventDescriptor))]
    [JsonSerializable(typeof(WebEventDescriptor))]
    [JsonSerializable(typeof(EventArgs))]
    [JsonSerializable(typeof(ChangeEventArgs))]
    [JsonSerializable(typeof(ClipboardEventArgs))]
    [JsonSerializable(typeof(DragEventArgs))]
    [JsonSerializable(typeof(ErrorEventArgs))]
    [JsonSerializable(typeof(FocusEventArgs))]
    [JsonSerializable(typeof(KeyboardEventArgs))]
    [JsonSerializable(typeof(MouseEventArgs))]
    [JsonSerializable(typeof(PointerEventArgs))]
    [JsonSerializable(typeof(ProgressEventArgs))]
    [JsonSerializable(typeof(TouchEventArgs))]
    [JsonSerializable(typeof(WheelEventArgs))]
    internal sealed partial class WebEventJsonContext : JsonSerializerContext
    {
    }
}
