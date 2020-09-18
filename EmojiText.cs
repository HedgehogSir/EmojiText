/*******************************************************************
* Copyright(c) 2020 tianci
* All rights reserved.
*
* 文件名称: EmojiText.cs 1.0
* Unity版本: 2018.4.6f1
* 简要描述:
* 
* 创建日期: 2020/06/30 11:36:30
* 作者:     tianci
* 说明:  
******************************************************************/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UF
{
    public class EmojiText : Text, IPointerClickHandler
    {
        #region 需要用到的数据类

        private class MatchResult
        {
            public int Width;
            public int Height;
            private Color mColor;
            private string mUpColorStr;
            private Color mDownColor;
            private string mDownColorStr;
            public string Prefix;
            public string CustomInfo;
            public EmojiType Type;
            public string Url;

            private void Reset()
            {
                Type = EmojiType.None;
                Prefix = string.Empty;
                Width = 0;
                Height = 0;
                mUpColorStr = string.Empty;
                Url = string.Empty;
                CustomInfo = string.Empty;
            }

            public void Parse(Match match, int fontSize)
            {
                Reset();
                if (!match.Success || match.Groups.Count != 8)
                    return;
                Prefix = match.Groups[1].Value;
                if (match.Groups[2].Success)
                {
                    var widthHeigthStr = match.Groups[2].Value;
                    var sp = widthHeigthStr.Split('|');
                    Width = sp.Length > 1 ? int.Parse(sp[1]) : fontSize;
                    Height = sp.Length == 3 ? int.Parse(sp[2]) : Width;
                }
                else
                {
                    Width = fontSize;
                    Height = Width;
                }
                if (match.Groups[4].Success && !string.IsNullOrEmpty(match.Groups[4].Value))
                    mUpColorStr = match.Groups[4].Value.Substring(0, 7);
                if (match.Groups[5].Success) mDownColorStr = match.Groups[5].Value;
                if (match.Groups[6].Success) Url = match.Groups[6].Value.Substring(1);
                if (match.Groups[7].Success) CustomInfo = match.Groups[7].Value.Substring(1);
                if (Prefix.Equals("0x01"))
                {
                    if (!string.IsNullOrEmpty(Url) && !string.IsNullOrEmpty(CustomInfo))
                        Type = EmojiType.HyperLink;
                }
                else if (Prefix.Equals("0x02"))
                {
                    if (!string.IsNullOrEmpty(CustomInfo))
                        Type = EmojiType.Texture;
                }
                else Type = EmojiType.Emoji;
            }

            public Color GetColor(Color fontColor)
            {
                if (string.IsNullOrEmpty(mUpColorStr))
                    return fontColor;
                ColorUtility.TryParseHtmlString(mUpColorStr, out mColor);
                return mColor;
            }

            public Color GetGradientColor(Color fontColor)
            {
                if (string.IsNullOrEmpty(mDownColorStr))
                    return GetColor(fontColor);
                ColorUtility.TryParseHtmlString(mDownColorStr, out mDownColor);
                return mDownColor;
            }

            public string GetHtmlString(Color fontColor)
            {
                return !string.IsNullOrEmpty(mUpColorStr) ? mUpColorStr : ColorUtility.ToHtmlStringRGBA(fontColor);
            }
        }

        private class TableInfo
        {
            public int Frame;
            public int Index;
        }

        private class TextureInfo
        {
            public Image Img;
            public int Index;
            public Vector3 Pos;
            public RectTransform RectTrans;
            public string CustomInfo;
        }

        private class EmojiInfo
        {
            public int Height;
            public TableInfo Table;
            public TextureInfo TexInfo;
            public EmojiType Type;
            public int Width;
        }

        public enum EmojiType
        {
            None,

            /// <summary>
            /// 将表情打包成图集
            /// </summary>
            Emoji,

            /// <summary>
            /// 超链接类型 0x01
            /// </summary>
            HyperLink,

            /// <summary>
            /// 自定义图片 0x02
            /// </summary>
            Texture
        }

        private class HrefInfo
        {
            public string Url;
            public bool IsShowUnderline;
            public Color UpColor;
            public int StartIndex;
            public int EndIndex;
            public Color DownColor;
            public readonly List<Rect> Boxes = new List<Rect>();
            public readonly List<Vector3> VertPosList = new List<Vector3>();
            public readonly List<Rect> UnderLineRectList = new List<Rect>();
        }
        #endregion

        #region 可以外部获取的字段

        /// <summary>
        /// 表情填充回调  0x02 
        /// </summary>
        public Action<Image, string> EmojiFillHandler;

        /// <summary>
        /// 超链接点击回调 0x01
        /// </summary>
        public Action<string> HyperLinkClick;

        /// <summary>
        /// 点击 Text 但是没有点击到超链接的回调
        /// </summary>
        public Action OutHyperLinkClick;

        /// <summary>
        /// 是否需要下划线
        /// </summary>
        public bool NeedUnderLine = true;
        #endregion
        
        private static Dictionary<string, TableInfo> mSpriteDict;
        
        private static readonly string mRegexTag = "\\[([0-9A-Za-z]+)((\\|[0-9]+){0,2})((#[0-9a-fA-F]{6}){0,2})(#[^=\\]]+)?(=[^\\]]+)?\\]";
        
        private readonly Dictionary<int, EmojiInfo> mEmojiDict = new Dictionary<int, EmojiInfo>();
        
        private readonly List<HrefInfo> mHrefList = new List<HrefInfo>();
        
        private readonly List<Image> mImageList = new List<Image>();
        
        private readonly MatchResult mMatchResult = new MatchResult();
        
        private readonly List<RectTransform> mRectTransList = new List<RectTransform>();
        
        private readonly StringBuilder mStrBuilder = new StringBuilder();
        
        private readonly UIVertex[] mTempVerts = new UIVertex[4];
        
        private string mOutputText = "";
        
        public override float preferredWidth
        {
            get
            {
                var settings = GetGenerationSettings(Vector2.zero);
                return cachedTextGeneratorForLayout.GetPreferredWidth(mOutputText, settings) / pixelsPerUnit;
            }
        }
        
        public override float preferredHeight
        {
            get
            {
                var settings = GetGenerationSettings(new Vector2(rectTransform.rect.size.x, 0.0f));
                return cachedTextGeneratorForLayout.GetPreferredHeight(mOutputText, settings) / pixelsPerUnit;
            }
        }

        /// <summary>
        /// 文本
        /// </summary>
        public override string text
        {
            get => m_Text;

            set
            {
                ParseText(value);
                base.text = value;
            }
        }
        
        public void OnPointerClick(PointerEventData eventData)
        {
            var isClickHyperLink = false;//是否点击在超链接范围内
            
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out var localPoint);
            for (var h = 0; h < mHrefList.Count; h++)
            {
                if (isClickHyperLink) break;

                var hrefInfo = mHrefList[h];
                for (var i = 0; i < hrefInfo.Boxes.Count; ++i)
                {
                    if (hrefInfo.Boxes[i].Contains(localPoint))
                    {
                        isClickHyperLink = true;
                        HyperLinkClick?.Invoke(hrefInfo.Url);
                        break;
                    }
                }
            }

            if (!isClickHyperLink)
                OutHyperLinkClick?.Invoke();
        }

        protected override void Awake()
        {
            base.Awake();

            if (mSpriteDict == null)
            {
                mSpriteDict = new Dictionary<string, TableInfo>();
#if UNITY_EDITOR
                var dir = new System.IO.DirectoryInfo(Application.dataPath);
                var files = dir.GetFiles("EmojiText.txt", System.IO.SearchOption.AllDirectories);
                var index = files[0].FullName.IndexOf("Assets");
                var tablePath = files[0].FullName.Substring(index).Replace('\\', '/');
                var emojiTable = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(tablePath).text;
                material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(tablePath.Replace("txt", "mat"));
#else
                var emojiTable = Resoreces.Load<TextAsset>("");//TODO
                material = Resoreces.Load<TextAsset>("");//TODO
#endif
                var lines = emojiTable.Split('\n');
                for (var i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrEmpty(lines[i]))
                        continue;
                    var strs = lines[i].Split('\t');
                    var info = new TableInfo { Frame = int.Parse(strs[1]), Index = int.Parse(strs[2]) };
                    mSpriteDict.Add(strs[0], info);
                }
            }
#if UNITY_EDITOR
            HyperLinkClick = (url) => Debug.Log("点击超链接区域:" + url);
            OutHyperLinkClick = () => Debug.Log("点击到非超链接区域");
            EmojiFillHandler = (img, customInfo) =>
            {
                Debug.Log("Emoji表情填充完毕");
                var dir = new System.IO.DirectoryInfo(Application.dataPath);
                var files = dir.GetFiles(customInfo + ".png", System.IO.SearchOption.AllDirectories);
                var index = files[0].FullName.IndexOf("Assets");
                var texPath = files[0].FullName.Substring(index).Replace('\\', '/');
                var tex2D = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if(tex2D != null)
                    img.sprite = Sprite.Create(tex2D, new Rect(Vector2.zero, new Vector2(tex2D.width, tex2D.height)), Vector2.zero);
            };
#endif
        }

        protected override void OnPopulateMesh(VertexHelper toFill)
        {
            if (font == null)
                return;
            ParseText(m_Text);
            m_DisableFontTextureRebuiltCallback = true;
            var extents = rectTransform.rect.size;
            var settings = GetGenerationSettings(extents);
            cachedTextGenerator.Populate(mOutputText, settings);
            var verts = cachedTextGenerator.verts;
            var unitsPerPixel = 1 / pixelsPerUnit;
            var vertCount = verts.Count - 4;
            if (vertCount <= 0)
            {
                toFill.Clear();
                return;
            }
            var repairVec = new Vector3(0, fontSize * 0.3f, 0);
            var roundingOffset = new Vector2(verts[0].position.x, verts[0].position.y) * unitsPerPixel;
            roundingOffset = PixelAdjustPoint(roundingOffset) - roundingOffset;
            toFill.Clear();
            if (roundingOffset != Vector2.zero)
            {
                for (var i = 0; i < vertCount; ++i)
                {
                    var tempVertsIndex = i & 3;
                    mTempVerts[tempVertsIndex] = verts[i];
                    mTempVerts[tempVertsIndex].position *= unitsPerPixel;
                    mTempVerts[tempVertsIndex].position.x += roundingOffset.x;
                    mTempVerts[tempVertsIndex].position.y += roundingOffset.y;
                    if (tempVertsIndex == 3)
                        toFill.AddUIVertexQuad(mTempVerts);
                }
            }
            else
            {
                var uv = Vector2.zero;
                for (var i = 0; i < vertCount; ++i)
                {
                    var index = i / 4;
                    var tempVertIndex = i & 3;
                    if (mEmojiDict.TryGetValue(index, out var info))
                    {
                        mTempVerts[tempVertIndex] = verts[i];
                        mTempVerts[tempVertIndex].position -= repairVec;
                        if (info.Type == EmojiType.Emoji)
                        {
                            uv.x = info.Table.Index;
                            uv.y = info.Table.Frame;
                            mTempVerts[tempVertIndex].uv0 += uv * 10;
                        }
                        else
                        {
                            if (tempVertIndex == 3)
                                info.TexInfo.Pos = mTempVerts[tempVertIndex].position;
                            mTempVerts[tempVertIndex].position = mTempVerts[0].position;
                        }

                        mTempVerts[tempVertIndex].position *= unitsPerPixel;
                        if (tempVertIndex == 3)
                            toFill.AddUIVertexQuad(mTempVerts);
                    }
                    else
                    {
                        mTempVerts[tempVertIndex] = verts[i];
                        mTempVerts[tempVertIndex].position *= unitsPerPixel;
                        if (tempVertIndex == 3)
                            toFill.AddUIVertexQuad(mTempVerts);
                    }
                }
                ComputeBoundsInfo(toFill);
                if (NeedUnderLine) DrawUnderLine(toFill);
                TwoColorGradient(toFill);
            }
            m_DisableFontTextureRebuiltCallback = false;
            StartCoroutine(ShowImages());
        }
        
        private void ParseText(string inputText)
        {
            if (mSpriteDict == null || !Application.isPlaying)// || !Application.isPlaying
            {
                mOutputText = inputText;
                return;
            }
            mStrBuilder.Clear();
            mEmojiDict.Clear();
            mHrefList.Clear();
            ClearChildrenImage();
            var matches = Regex.Matches(inputText, mRegexTag);
            if (matches.Count > 0)
            {
                var textIndex = 0;
                var imgIdx = 0;
                var rectIdx = 0;
                for (var i = 0; i < matches.Count; i++)
                {
                    var match = matches[i];
                    mMatchResult.Parse(match, fontSize);
                    switch (mMatchResult.Type)
                    {
                        case EmojiType.Emoji:
                            {
                                if (mSpriteDict.TryGetValue(mMatchResult.Prefix, out var info))
                                {
                                    mStrBuilder.Append(inputText.Substring(textIndex, match.Index - textIndex));
                                    var temIndex = mStrBuilder.Length;
                                    mStrBuilder.Append("<quad size=");
                                    mStrBuilder.Append(mMatchResult.Height);
                                    mStrBuilder.Append(" width=");
                                    mStrBuilder.Append((mMatchResult.Width * 1.0f / mMatchResult.Height).ToString("f2"));
                                    mStrBuilder.Append(" />");
                                    mEmojiDict.Add(temIndex, new EmojiInfo
                                    {
                                        Type = EmojiType.Emoji,
                                        Table = info,
                                        Width = mMatchResult.Width,
                                        Height = mMatchResult.Height
                                    });
                                    if (!string.IsNullOrEmpty(mMatchResult.Url))
                                    {
                                        var hrefInfo = new HrefInfo
                                        {
                                            IsShowUnderline = false,
                                            StartIndex = temIndex * 4,
                                            EndIndex = temIndex * 4 + 3,
                                            Url = mMatchResult.Url,
                                            UpColor = mMatchResult.GetColor(color),
                                            DownColor = mMatchResult.GetGradientColor(color)
                                        };
                                        mHrefList.Add(hrefInfo);
                                    }
                                    textIndex = match.Index + match.Length;
                                }
                                break;
                            }
                        case EmojiType.HyperLink:
                            {
                                mStrBuilder.Append(inputText.Substring(textIndex, match.Index - textIndex));
                                mStrBuilder.Append("<color=");
                                mStrBuilder.Append(mMatchResult.GetHtmlString(color));
                                mStrBuilder.Append(">");
                                var href = new HrefInfo { IsShowUnderline = true, StartIndex = mStrBuilder.Length * 4 };
                                mStrBuilder.Append(mMatchResult.CustomInfo);
                                href.EndIndex = mStrBuilder.Length * 4 - 1;
                                href.Url = mMatchResult.Url;
                                href.UpColor = mMatchResult.GetColor(color);
                                href.DownColor = mMatchResult.GetGradientColor(color);
                                mHrefList.Add(href);
                                mStrBuilder.Append("</color>");
                                textIndex = match.Index + match.Length;
                                break;
                            }
                        case EmojiType.Texture:
                            {
                                mStrBuilder.Append(inputText.Substring(textIndex, match.Index - textIndex));
                                var temIndex = mStrBuilder.Length;
                                mStrBuilder.Append("<quad size=");
                                mStrBuilder.Append(mMatchResult.Height);
                                mStrBuilder.Append(" width=");
                                mStrBuilder.Append((mMatchResult.Width * 1.0f / mMatchResult.Height).ToString("f2"));
                                mStrBuilder.Append(" />");
                                mEmojiDict.Add(temIndex, new EmojiInfo
                                {
                                    Type = mMatchResult.Type,
                                    Width = mMatchResult.Width,
                                    Height = mMatchResult.Height,
                                    TexInfo = new TextureInfo
                                    {
                                        CustomInfo = mMatchResult.CustomInfo,
                                        Index = mMatchResult.Type == EmojiType.Texture ? imgIdx++ : rectIdx++
                                    }
                                });
                                if (!string.IsNullOrEmpty(mMatchResult.Url))
                                {
                                    var hrefInfo = new HrefInfo
                                    {
                                        IsShowUnderline = false,
                                        StartIndex = temIndex * 4,
                                        EndIndex = temIndex * 4 + 3,
                                        Url = mMatchResult.Url,
                                        UpColor = mMatchResult.GetColor(color),
                                        DownColor = mMatchResult.GetGradientColor(color)
                                    };
                                    mHrefList.Add(hrefInfo);
                                }
                                textIndex = match.Index + match.Length;
                                break;
                            }
                    }
                }
                mStrBuilder.Append(inputText.Substring(textIndex, inputText.Length - textIndex));
                mOutputText = mStrBuilder.ToString();
            }
            else mOutputText = inputText;
        }

        /// <summary>
        /// 计算边界信息
        /// </summary>
        /// <param name="toFill"></param>
        private void ComputeBoundsInfo(VertexHelper toFill)
        {
            var vert = new UIVertex();
            for (var u = 0; u < mHrefList.Count; u++)
            {
                var underline = mHrefList[u];
                underline.Boxes.Clear();
                underline.VertPosList.Clear();
                underline.UnderLineRectList.Clear();
                if (underline.StartIndex >= toFill.currentVertCount)
                    continue;
                toFill.PopulateUIVertex(ref vert, underline.StartIndex);
                var pos = vert.position;
                var bounds = new Bounds(pos, Vector3.zero);
                for (int i = underline.StartIndex; i < underline.EndIndex; i++)
                {
                    if (i >= toFill.currentVertCount)
                        break;
                    toFill.PopulateUIVertex(ref vert, i);
                    pos = vert.position;
                    underline.VertPosList.Add(pos);
                    if (pos.x < bounds.min.x)
                    {
                        underline.Boxes.Add(new Rect(bounds.min, bounds.size));
                        bounds = new Bounds(pos, Vector3.zero);
                    }
                    else bounds.Encapsulate(pos);
                }
                underline.Boxes.Add(new Rect(bounds.min, bounds.size));

                toFill.PopulateUIVertex(ref vert, underline.EndIndex);
                pos = vert.position;
                underline.VertPosList.Add(pos);

                float height = (underline.VertPosList[1].y - underline.VertPosList[3].y) / 5;//1/5作为下划线的高度
                float yPos = underline.VertPosList[3].y - height / 2;//首字符的 Y 值决定剩余下划线的 Y 值
                for (int i = 0; i < underline.VertPosList.Count; i += 4)
                {
                    if (i > 0)
                    {
                        if (Math.Abs(underline.VertPosList[i + 3].y - yPos) > height * 3)//高度偏差太大说明换行了
                        {
                            yPos = underline.VertPosList[i + 3].y - height / 2;
                        }
                        else
                        {
                            var lastXRight = underline.VertPosList[i - 2].x;
                            var xLeft = underline.VertPosList[i].x;
                            if (xLeft > lastXRight)//需要在衔接断层处填充下划线
                            {
                                float spaceWidth = xLeft - lastXRight;
                                float spaceXPos = lastXRight;
                                underline.UnderLineRectList.Add(new Rect(new Vector2(spaceXPos, yPos), new Vector2(spaceWidth, height)));
                            }
                        }
                    }

                    float width = underline.VertPosList[i + 1].x - underline.VertPosList[i + 3].x;
                    float xPos = underline.VertPosList[i].x;
                    underline.UnderLineRectList.Add(new Rect(new Vector2(xPos, yPos), new Vector2(width, height)));
                }
            }
        }

        /// <summary>
        /// 绘制下划线
        /// </summary>
        /// <param name="toFill"></param>
        private void DrawUnderLine(VertexHelper toFill)
        {
            if (mHrefList.Count <= 0)
                return;
            var extents = rectTransform.rect.size;
            var settings = GetGenerationSettings(extents);
            cachedTextGenerator.Populate("_", settings);
            var uList = cachedTextGenerator.verts;
            var h = uList[2].position.y - uList[1].position.y;
            var temVecs = new Vector3[4];
            for (var i = 0; i < mHrefList.Count; i++)
            {
                var info = mHrefList[i];
                if (!info.IsShowUnderline) continue;

                for (int j = 0; j < info.UnderLineRectList.Count; j++)
                {
                    var center = info.UnderLineRectList[j].center;
                    var halfWidth = info.UnderLineRectList[j].size.x / 2 * 1.35f;//由于 "_" 的渲染结果，两边有一些渐变
                    var min = info.UnderLineRectList[j].min;
                    var max = info.UnderLineRectList[j].max;
                    temVecs[0] = new Vector3(center.x - halfWidth, max.y);
                    temVecs[1] = new Vector3(center.x + halfWidth, max.y);
                    temVecs[2] = new Vector3(center.x + halfWidth, min.y);
                    temVecs[3] = new Vector3(center.x - halfWidth, min.y);

                    for (int k = 0; k < 4; k++)
                    {
                        mTempVerts[k] = uList[k];
                        mTempVerts[k].color = info.DownColor;
                        mTempVerts[k].position = temVecs[k];
                    }

                    toFill.AddUIVertexQuad(mTempVerts);
                }
            }
        }
        
        private static UIVertex mTmpVert;
        /// <summary>
        /// 双色渐变
        /// </summary>
        /// <param name="toFill"></param>
        private void TwoColorGradient(VertexHelper toFill)
        {
            foreach (var hrefInfo in mHrefList)
            {
                if (hrefInfo.StartIndex >= toFill.currentVertCount)
                    continue;
                for (int i = hrefInfo.StartIndex, m = hrefInfo.EndIndex; i < m; i += 4)
                {
                    if (i >= toFill.currentVertCount)
                        break;
                    toFill.PopulateUIVertex(ref mTmpVert, i);
                    mTmpVert.color = hrefInfo.UpColor;
                    toFill.SetUIVertex(mTmpVert, i);
                    toFill.PopulateUIVertex(ref mTmpVert, i + 1);
                    mTmpVert.color = hrefInfo.UpColor;
                    toFill.SetUIVertex(mTmpVert, i + 1);
                    toFill.PopulateUIVertex(ref mTmpVert, i + 2);
                    mTmpVert.color = hrefInfo.DownColor;
                    toFill.SetUIVertex(mTmpVert, i + 2);
                    toFill.PopulateUIVertex(ref mTmpVert, i + 3);
                    mTmpVert.color = hrefInfo.DownColor;
                    toFill.SetUIVertex(mTmpVert, i + 3);
                }
            }
        }
        
        private void ClearChildrenImage()
        {
            for (var i = 0; i < mImageList.Count; i++) mImageList[i].rectTransform.localScale = Vector3.zero;
            for (var i = 0; i < mRectTransList.Count; i++) mRectTransList[i].localScale = Vector3.zero;
        }
        
        private IEnumerator ShowImages()
        {
            yield return null;
            foreach (var emojiInfo in mEmojiDict.Values)
            {
                if (emojiInfo.Type == EmojiType.Texture)
                {
                    emojiInfo.TexInfo.Img = GetImage(emojiInfo.TexInfo, emojiInfo.Width, emojiInfo.Height);

                    EmojiFillHandler?.Invoke(emojiInfo.TexInfo.Img, emojiInfo.TexInfo.CustomInfo);
                }
            }
        }
        
        private Image GetImage(TextureInfo info, int width, int height)
        {
            Image img = null;
            if (mImageList.Count > info.Index)
                img = mImageList[info.Index];
            if (img == null)
            {
                var obj = new GameObject("Emoji_" + info.Index);
                img = obj.AddComponent<Image>();
                obj.transform.SetParent(transform);
                obj.transform.localPosition = Vector3.zero;
                img.rectTransform.pivot = Vector2.zero;
                img.raycastTarget = false;
                if (mImageList.Count > info.Index)
                    mImageList[info.Index] = img;
                else
                    mImageList.Add(img);
            }
            img.rectTransform.localScale = Vector3.one;
            img.rectTransform.sizeDelta = new Vector2(width, height);
            img.rectTransform.anchoredPosition = info.Pos;
            return img;
        }
    }
}
