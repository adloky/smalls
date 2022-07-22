using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace ConApp {
    public class JsonHelper {
        private const string NONAME_ELEMENT_NAME = "noname_50638a62";
        private const string AUTOROOT_ELEMENT_NAME = "root";

        internal static void Arralize(ref JToken jToken, string preArrayName = "items") {
            jToken.Children().ToList().ForEach(child => {
                var childProp = child as JProperty;
                if (childProp != null) preArrayName = childProp.Name;
                Arralize(ref child, preArrayName);
            });

            if (jToken is JObject) {
                jToken = new JArray { jToken };
            }
            else if (jToken is JArray) {
                var jParentArray = jToken.Parent as JArray;
                if (jParentArray != null) {
                    var index = jParentArray.IndexOf(jToken);
                    jParentArray[index] = new JObject(new JProperty(preArrayName, jToken));
                }
            }
            else if (jToken is JProperty) {
                var jProp = jToken as JProperty;
                if (!(jProp.Value is JArray)) {
                    jProp.Value = new JArray { jProp.Value };
                }
            }
        }

        private static string getTokenName(JToken jToken) {
            if (jToken == null) {
                return NONAME_ELEMENT_NAME;
            }

            var jProp = jToken as JProperty;

            var resultName = jProp?.Name;

            if (resultName == null) {
                throw new Exception("Token must be property");
            }

            return resultName;
        }

        private static XNode[] getXNodeTree(JToken jToken, JToken parentJToken = null) {
            var xNodeSet = jToken.Children().SelectMany(item => {
                var xElem = new XElement(getTokenName(parentJToken));
                var jObj = item as JObject;
                if (jObj == null) {
                    xElem.Add(((JValue)item).Value);
                }
                else {
                    var subElems = jObj.Children().SelectMany(prop => getXNodeTree((prop as JProperty)?.Value, prop as JProperty));
                    if (parentJToken == null) {
                        return subElems;
                    }
                    xElem.Add(subElems);
                }
                return Enumerable.Repeat(xElem, 1);
            }).ToArray();

            return xNodeSet;
        }

        private static XNode JsonToXml(JToken jToken) {
            var jTokenArralized = jToken.DeepClone();
            Arralize(ref jTokenArralized);
            var xNodeTree = getXNodeTree(jTokenArralized);

            var resultElem = new XElement(AUTOROOT_ELEMENT_NAME, xNodeTree.OfType<object>());

            return resultElem;
        }

        public static XNode JsonToXml(string json) {
            return JsonToXml(JToken.Parse(json));
        }
    }
}
