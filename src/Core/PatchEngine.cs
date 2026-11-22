using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace BongoCatAutoClaim.Core;

public sealed record PatchResult(bool Success, bool AlreadyPatched, string Message, byte[]? PatchedBytes = null);

/// <summary>
/// Reads Assembly-CSharp.dll with System.Reflection.Metadata (the game assembly is never loaded or executed),
/// resolves the current metadata tokens and builds the in-place IL patch.
/// </summary>
public static class PatchEngine
{
    public static PatchResult Analyze(byte[] rawBytes, bool inspectOnly)
    {
        try
        {
            using var pe = new PEReader(ImmutableArray.Create(rawBytes));
            MetadataReader md = pe.GetMetadataReader();

            TypeDefinitionHandle shop = FindTopLevelType(md, "BongoCat", "Shop");
            TypeDefinitionHandle shopItem = FindTopLevelType(md, "BongoCat", "ShopItem");
            TypeDefinitionHandle osa = FindTopLevelType(md, "", "OpenScaleAnimation");

            if (shop.IsNil || shopItem.IsNil)
            {
                return Fail("BongoCat.Shop or BongoCat.ShopItem not found in assembly.");
            }

            MethodDefinitionHandle mOnChestReady = FindMethod(md, shop, "OnChestReady");
            MethodDefinitionHandle mCanGetShown = FindMethod(md, shop, "get_CanGetShownChest");
            if (mCanGetShown.IsNil) mCanGetShown = FindMethod(md, shop, "get_CanAffordChest");
            FieldDefinitionHandle fSentChestReady = FindField(md, shop, "_sentChestReady");
            FieldDefinitionHandle fShopVisuals = FindField(md, shop, "_shopVisuals");
            FieldDefinitionHandle fShopItem = FindField(md, shop, "_shopItem");
            MethodDefinitionHandle mBuy = FindMethod(md, shopItem, "Buy");
            MethodDefinitionHandle mShow = osa.IsNil ? default : FindMethod(md, osa, "Show");
            MethodDefinitionHandle mGetIsShown = osa.IsNil ? default : FindMethod(md, osa, "get_IsShown");

            if (mOnChestReady.IsNil || mCanGetShown.IsNil || fShopItem.IsNil || mBuy.IsNil)
            {
                return Fail("Target methods or fields missing on BongoCat.Shop.");
            }

            byte[] ilOnChestReady = GetIl(pe, md, mOnChestReady);
            byte[] buyCallSeq = TokenInstruction(0x6F, Token(mBuy));

            if (FindSequence(ilOnChestReady, buyCallSeq).Count > 0)
            {
                return new PatchResult(true, true, "Assembly is patched.");
            }

            if (inspectOnly)
            {
                return new PatchResult(true, false, "Assembly is unpatched.");
            }

            List<int> onChestReadyOffsets = FindSequence(rawBytes, ilOnChestReady);
            if (onChestReadyOffsets.Count != 1)
            {
                return Fail("Could not uniquely locate OnChestReady body (matches: " + onChestReadyOffsets.Count + ").");
            }

            int patchStartInMethod = 0;
            if (!fSentChestReady.IsNil)
            {
                byte[] stfldSent = TokenInstruction(0x7D, Token(fSentChestReady));
                List<int> stfldMatches = FindSequence(ilOnChestReady, stfldSent);
                if (stfldMatches.Count > 0)
                {
                    patchStartInMethod = stfldMatches[0] + stfldSent.Length;
                }
            }

            int availableBytes = ilOnChestReady.Length - patchStartInMethod;
            var payload = new List<byte>();

            if (availableBytes >= 31 && !fShopVisuals.IsNil && !mShow.IsNil)
            {
                // if (this.CanGetShownChest) { this._shopVisuals.Show(); this._shopItem.Buy(); } return;
                payload.Add(0x02);
                payload.AddRange(TokenInstruction(0x28, Token(mCanGetShown)));
                payload.Add(0x2C);
                payload.Add(0x16);
                payload.Add(0x02);
                payload.AddRange(TokenInstruction(0x7B, Token(fShopVisuals)));
                payload.AddRange(TokenInstruction(0x6F, Token(mShow)));
                payload.Add(0x02);
                payload.AddRange(TokenInstruction(0x7B, Token(fShopItem)));
                payload.AddRange(TokenInstruction(0x6F, Token(mBuy)));
                payload.Add(0x2A);
            }
            else if (availableBytes >= 20)
            {
                // if (this.CanGetShownChest) { this._shopItem.Buy(); } return;
                payload.Add(0x02);
                payload.AddRange(TokenInstruction(0x28, Token(mCanGetShown)));
                payload.Add(0x2C);
                payload.Add(0x0B);
                payload.Add(0x02);
                payload.AddRange(TokenInstruction(0x7B, Token(fShopItem)));
                payload.AddRange(TokenInstruction(0x6F, Token(mBuy)));
                payload.Add(0x2A);
            }
            else
            {
                return Fail("Insufficient IL body length in OnChestReady (" + availableBytes + " bytes).");
            }

            while (payload.Count < availableBytes)
            {
                payload.Add(0x00);
            }

            byte[] patched = (byte[])rawBytes.Clone();
            int filePatchOffset = onChestReadyOffsets[0] + patchStartInMethod;
            payload.CopyTo(patched, filePatchOffset);

            // TimerUpdate fallback: drop "!this._shopVisuals.IsShown" so OnChestReady keeps firing while the popup is visible
            foreach (TypeDefinitionHandle nested in md.GetTypeDefinition(shop).GetNestedTypes())
            {
                string nestedName = md.GetString(md.GetTypeDefinition(nested).Name);
                if (!nestedName.StartsWith("<TimerUpdate>d__", StringComparison.Ordinal)) continue;

                MethodDefinitionHandle mMoveNext = FindMethod(md, nested, "MoveNext");
                if (mMoveNext.IsNil || fShopVisuals.IsNil || mGetIsShown.IsNil) continue;

                byte[] ilMoveNext = GetIl(pe, md, mMoveNext);
                List<int> moveNextOffsets = FindSequence(patched, ilMoveNext);
                if (moveNextOffsets.Count != 1) continue;

                var isShownCheck = new List<byte> { 0x07 };
                isShownCheck.AddRange(TokenInstruction(0x7B, Token(fShopVisuals)));
                isShownCheck.AddRange(TokenInstruction(0x6F, Token(mGetIsShown)));
                isShownCheck.Add(0x2D);
                isShownCheck.Add(0x06);

                List<int> checkMatches = FindSequence(ilMoveNext, isShownCheck.ToArray());
                if (checkMatches.Count == 1)
                {
                    int checkFileOffset = moveNextOffsets[0] + checkMatches[0];
                    for (int i = 0; i < isShownCheck.Count; i++)
                    {
                        patched[checkFileOffset + i] = 0x00;
                    }
                }
            }

            return new PatchResult(true, false, "Patch built.", patched);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static PatchResult Fail(string message) => new(false, false, message);

    private static int Token(EntityHandle handle) => MetadataTokens.GetToken(handle);

    private static TypeDefinitionHandle FindTopLevelType(MetadataReader md, string ns, string name)
    {
        foreach (TypeDefinitionHandle h in md.TypeDefinitions)
        {
            TypeDefinition td = md.GetTypeDefinition(h);
            if (!td.GetDeclaringType().IsNil) continue;
            if (md.StringComparer.Equals(td.Namespace, ns) && md.StringComparer.Equals(td.Name, name))
            {
                return h;
            }
        }
        return default;
    }

    /// <summary>Declared method lookup by name. Overloads are rejected, matching Type.GetMethod(name, flags).</summary>
    private static MethodDefinitionHandle FindMethod(MetadataReader md, TypeDefinitionHandle type, string name)
    {
        MethodDefinitionHandle found = default;
        foreach (MethodDefinitionHandle h in md.GetTypeDefinition(type).GetMethods())
        {
            if (!md.StringComparer.Equals(md.GetMethodDefinition(h).Name, name)) continue;
            if (!found.IsNil)
            {
                throw new InvalidOperationException("Ambiguous match found for " + name + ".");
            }
            found = h;
        }
        return found;
    }

    private static FieldDefinitionHandle FindField(MetadataReader md, TypeDefinitionHandle type, string name)
    {
        foreach (FieldDefinitionHandle h in md.GetTypeDefinition(type).GetFields())
        {
            if (md.StringComparer.Equals(md.GetFieldDefinition(h).Name, name)) return h;
        }
        return default;
    }

    private static byte[] GetIl(PEReader pe, MetadataReader md, MethodDefinitionHandle method)
    {
        int rva = md.GetMethodDefinition(method).RelativeVirtualAddress;
        if (rva == 0) return Array.Empty<byte>();
        return pe.GetMethodBody(rva).GetILBytes() ?? Array.Empty<byte>();
    }

    private static byte[] TokenInstruction(byte opcode, int token)
    {
        byte[] t = BitConverter.GetBytes(token);
        return new[] { opcode, t[0], t[1], t[2], t[3] };
    }

    private static List<int> FindSequence(byte[] haystack, byte[] needle)
    {
        var results = new List<int>();
        if (needle.Length == 0) return results;
        int limit = haystack.Length - needle.Length;
        for (int i = 0; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) results.Add(i);
        }
        return results;
    }
}
