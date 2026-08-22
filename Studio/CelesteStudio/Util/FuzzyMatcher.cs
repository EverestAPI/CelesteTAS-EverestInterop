using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CelesteStudio.Util;

/// Fuzzy-search implementation based on https://github.com/helix-editor/nucleo
public class FuzzyMatcher : IDisposable {
    private unsafe class Matrix : IDisposable {
        private const int MAX_MATRIX_LEN = 100 * 1024; // 100kB
        
        // These two aren't hard maxima, instead we simply allow whatever will fit into memory
        private const int MAX_HAYSTACK_LEN = 2048; // 64kB
        private const int MAX_NEEDLE_LEN = 2048; // 64kB

        public readonly struct ScoreCell(ushort score, byte consecutiveBonus, bool matched) {
            public static readonly ScoreCell Unmatched = new ScoreCell(0, 0, true);
            
            public readonly ushort Score = score;
            public readonly byte ConsecutiveBonus = consecutiveBonus;
            public readonly bool Matched = matched;
        }

        public readonly struct MatcherData(Memory<char> haystack, Memory<byte> bonus, Memory<ushort> rowOffset, Memory<ScoreCell> scoreCells, Memory<byte> matrixCells) {
            private const int MAX_HAYSTACK_SIZE = sizeof(char) * MAX_HAYSTACK_LEN;
            public readonly Memory<char> Haystack = haystack;

            private const int MAX_BONUS_SIZE = sizeof(byte) * MAX_HAYSTACK_LEN;
            public readonly Memory<byte> Bonus = bonus;
            
            private const int MAX_ROW_OFFSETS_SIZE = sizeof(ushort) * MAX_NEEDLE_LEN;
            public readonly Memory<ushort> RowOffset = rowOffset;

            private static readonly int MAX_SCORE_CELLS_SIZE = sizeof(ScoreCell) * MAX_NEEDLE_LEN;
            public readonly Memory<ScoreCell> ScoreCells = scoreCells;
            
            private const int MAX_MATRIX_CELLS_SIZE = sizeof(byte) * MAX_MATRIX_LEN;
            private const byte MATRIX_P_MATCH_MASK = 0b00000001;
            private const byte MATRIX_M_MATCH_MASK = 0b00000010;
            public readonly Memory<byte> MatrixCells = matrixCells;

            public static readonly nuint MAX_SIZE = (nuint)(MAX_HAYSTACK_SIZE + MAX_BONUS_SIZE + MAX_ROW_OFFSETS_SIZE + MAX_SCORE_CELLS_SIZE + MAX_MATRIX_CELLS_SIZE);

            public bool Setup(FuzzyMatcher matcher, ReadOnlySpan<char> needle, CharClass prevClass, bool indices) {
                var haystackSpan = Haystack.Span;
                var bonusSpan = Bonus.Span;
                var rowOffsetSpan = RowOffset.Span;

                int rowIdx = 0;
                char needleChar = matcher.GetCharNormalized(needle[0]);

                bool matched = false;
                for (int colIdx = 0; colIdx < Haystack.Length; colIdx++) {
                    (char currChar, var currClass) = matcher.GetCharClassNormalized(haystackSpan[colIdx]);
                    haystackSpan[colIdx] = currChar;
                    bonusSpan[colIdx] = (byte)GetClassBonus(prevClass, currClass);

                    prevClass = currClass;

                    if (currChar == needleChar) {
                        // Save the first idx of each char
                        if (rowIdx + 1 < needle.Length) {
                            rowOffsetSpan[rowIdx] = (ushort)colIdx;

                            rowIdx++;
                            needleChar = matcher.GetCharNormalized(needle[rowIdx]);
                        } else if (!matched) {
                            rowOffsetSpan[rowIdx] = (ushort)colIdx;
                            // We have at least one match
                            matched = true;
                        }
                    }
                }

                if (!matched) {
                    return false;
                }
                
                ScoreRow(
                    matcher,
                    ScoreCells.Span, MatrixCells.Span, 
                    haystackSpan, bonusSpan, 
                    rowOffset: 0, nextRowOffset: rowOffsetSpan[1],
                    needleIdx: 0, needleChar: matcher.GetCharNormalized(needle[0]), nextNeedleChar: matcher.GetCharNormalized(needle[1]),
                    firstRow: true, indices
                );
                return true;
            }

            public int Populate(FuzzyMatcher matcher, ReadOnlySpan<char> needle, bool indices) {
                var matrixCells = MatrixCells.Span[ScoreCells.Length..];
                int matrixCellsOffset = ScoreCells.Length;
                var rowOffsetSpan = RowOffset.Span;

                char currNeedleChar = matcher.GetCharNormalized(needle[1]);
                int currRowOffset = rowOffsetSpan[1];

                for (int nextNeedleIdx = 1; nextNeedleIdx < needle.Length - 1; nextNeedleIdx++) {
                    char nextNeedleChar = matcher.GetCharNormalized(needle[nextNeedleIdx + 1]);
                    int nextRowOffset = rowOffsetSpan[nextNeedleIdx + 1];
                    
                    ScoreRow(
                        matcher,
                        ScoreCells.Span, matrixCells,
                        Haystack.Span, Bonus.Span,
                        currRowOffset, nextRowOffset,
                        nextNeedleIdx, currNeedleChar, nextNeedleChar,
                        firstRow: false, indices
                    );

                    int len = ScoreCells.Length + nextNeedleIdx - currRowOffset;
                    matrixCells = matrixCells[len..];
                    matrixCellsOffset += len;

                    currNeedleChar = nextNeedleChar;
                    currRowOffset = nextRowOffset;
                }

                return matrixCellsOffset;
            }

            public void ReconstructOptimalPath(ushort maxScoreEnd, int matrixLen, int start, List<int> indicesList) {
                indicesList.EnsureCapacity(RowOffset.Length);
                indicesList.AddRange(Enumerable.Repeat(0, RowOffset.Length));
                var indices = CollectionsMarshal.AsSpan(indicesList);

                var rowOffsetSpan = RowOffset.Span;
                var scoreCellsSpan = ScoreCells.Span;

                int lastRowOffset = rowOffsetSpan[^1];
                indices[RowOffset.Length - 1] = start + maxScoreEnd + lastRowOffset;

                var matrixCells = MatrixCells.Span[..matrixLen];
                int width = ScoreCells.Length;
                
                int rowIdx = RowOffset.Length - 2;
                int rowOffset = rowOffsetSpan[rowIdx];
                int relativeRowOffset = rowOffset - rowIdx;

                int splitIdx = matrixCells.Length - (width - relativeRowOffset);
                var row = matrixCells[splitIdx..];
                matrixCells = matrixCells[..splitIdx];

                int col = maxScoreEnd;
                int relativeLastRowOffset = lastRowOffset + 1 - RowOffset.Length;
                
                bool matched = scoreCellsSpan[col + relativeLastRowOffset].Matched;
                col += lastRowOffset - rowOffset - 1;

                while (true) {
                    if (matched) {
                        indices[rowIdx] = start + col + rowOffset;
                    }

                    byte mask = matched ? MATRIX_M_MATCH_MASK : MATRIX_P_MATCH_MASK;
                    bool nextMatched = (row[col] & mask) != 0;

                    if (matched) {
                        if (rowIdx == 0) {
                            break;
                        }
                        
                        rowIdx--;
                        int nextRowOffset = rowOffsetSpan[rowIdx];
                        int nextRelativeRowOffset = nextRowOffset - rowIdx;
                        int nextSplitIdx = matrixCells.Length - (width - nextRelativeRowOffset);
                        var nextRow = matrixCells[nextSplitIdx..];
                        matrixCells = matrixCells[..nextSplitIdx];

                        col += rowOffset - nextRowOffset;
                        rowOffset = nextRowOffset;
                        row = nextRow;
                    }

                    col -= 1;
                    matched = nextMatched;
                }
            }
            
            private static void ScoreRow(
                FuzzyMatcher matcher,
                Span<ScoreCell> currentRow, Span<byte> matrixCells, 
                ReadOnlySpan<char> haystack, ReadOnlySpan<byte> bonus, 
                int rowOffset, int nextRowOffset,
                int needleIdx, char needleChar, char nextNeedleChar,
                bool firstRow, bool indices
            ) {
                nextRowOffset -= 1;

                int relativeRowOffset = rowOffset - needleIdx;
                int nextRelativeRowOffset = nextRowOffset - needleIdx;

                ushort prevPScore = 0;
                ushort prevMScore = 0;

                var skippedHaystack = haystack[rowOffset..nextRowOffset];
                var skippedBonus = bonus[rowOffset..nextRowOffset];
                var skippedScoreCells = currentRow[relativeRowOffset..nextRelativeRowOffset];
                var skippedMatrixCells = matrixCells;
                int skippedLength = nextRowOffset - rowOffset;
                for (int idx = 0; idx < skippedLength; idx++) {
                    (ushort pScore, bool pMatched) = PScore(prevPScore, prevMScore);

                    ScoreCell mCell;
                    if (firstRow) {
                        char currChar = matcher.GetCharNormalized(skippedHaystack[idx]);
                        mCell = currChar == needleChar 
                            ? new ScoreCell(score: (ushort)(skippedBonus[idx] * BONUS_FIRST_CHAR_MULTIPLIER + SCORE_MATCH), consecutiveBonus: skippedBonus[idx], matched: false) 
                            : ScoreCell.Unmatched;
                    } else {
                        mCell = skippedScoreCells[idx];
                    }

                    if (indices) {
                        skippedMatrixCells[idx] = (byte)((pMatched ? MATRIX_P_MATCH_MASK : 0) + (mCell.Matched ? MATRIX_M_MATCH_MASK : 0));
                    }

                    prevPScore = pScore;
                    prevMScore = mCell.Score;
                }

                var unskippedHaystack = haystack[nextRowOffset..];
                var unskippedBonus = bonus[nextRowOffset..];
                var unskippedScoreCells = currentRow[nextRelativeRowOffset..];
                var unskippedMatrixCells = matrixCells[(nextRelativeRowOffset - relativeRowOffset)..];
                int unskippedLength = Math.Min(haystack.Length - nextRowOffset - 1, currentRow.Length - nextRelativeRowOffset);
                for (int idx = 0; idx < unskippedLength; idx++) {
                    (ushort pScore, bool pMatched) = PScore(prevPScore, prevMScore);

                    ScoreCell mCell;
                    if (firstRow) {
                        char currChar = matcher.GetCharNormalized(unskippedHaystack[idx]);
                        mCell = currChar == needleChar 
                            ? new ScoreCell(score: (ushort)(unskippedBonus[idx] * BONUS_FIRST_CHAR_MULTIPLIER + SCORE_MATCH), consecutiveBonus: unskippedBonus[idx], matched: false) 
                            : ScoreCell.Unmatched;
                    } else {
                        mCell = unskippedScoreCells[idx];
                    }

                    unskippedScoreCells[idx] = unskippedHaystack[idx + 1] == nextNeedleChar
                        ? NextMCell(pScore, unskippedBonus[idx + 1], mCell)
                        : ScoreCell.Unmatched;

                    if (indices) {
                        unskippedMatrixCells[idx] = (byte)((pMatched ? MATRIX_P_MATCH_MASK : 0) + (mCell.Matched ? MATRIX_M_MATCH_MASK : 0));
                    }

                    prevPScore = pScore;
                    prevMScore = mCell.Score;
                }

                return;

                static (ushort, bool) PScore(ushort prevPScore, ushort prevMScore) {
                    ushort scoreMatch = (ushort)(prevMScore > PENALTY_GAP_START ? prevMScore - PENALTY_GAP_START : 0);
                    ushort scoreSkip = (ushort)(prevPScore > PENALTY_GAP_EXTENSION ? prevPScore - PENALTY_GAP_EXTENSION : 0);

                    if (scoreMatch > scoreSkip) {
                        return (scoreMatch, true);
                    } else {
                        return (scoreSkip, false);
                    }
                }

                static ScoreCell NextMCell(ushort pScore, ushort bonus, ScoreCell mCell) {
                    if (mCell is { Score: 0, ConsecutiveBonus: 0, Matched: true } /* == ScoreCell.Unmatched */) {
                        return new ScoreCell(score: (ushort)(pScore + bonus + SCORE_MATCH), consecutiveBonus: (byte)bonus, matched: false);
                    }

                    ushort consecutiveBonus = ushort.Max(mCell.ConsecutiveBonus, BONUS_CONSECUTIVE);
                    if (bonus >= BONUS_CONSECUTIVE && bonus > consecutiveBonus) {
                        consecutiveBonus = bonus;
                    }

                    ushort scoreMatch = (ushort)(mCell.Score + ushort.Max(consecutiveBonus, bonus));
                    ushort scoreSkip = (ushort)(pScore + bonus);
                    if (scoreMatch > scoreSkip) {
                        return new ScoreCell(score: (ushort)(scoreMatch + SCORE_MATCH), consecutiveBonus: (byte)consecutiveBonus, matched: true);
                    } else {
                        return new ScoreCell(score: (ushort)(scoreSkip + SCORE_MATCH), consecutiveBonus: (byte)bonus, matched: false);
                    }
                }
            }
        }

        private readonly void* buffer = NativeMemory.AlignedAlloc(MatcherData.MAX_SIZE, (nuint)UnsafeExtensions.AlignmentOf<nint>());

        ~Matrix() => Dispose();
        public void Dispose() {
            NativeMemory.AlignedFree(buffer);
            GC.SuppressFinalize(this);
        }

        /// Attempts to allocate a view for MatcherData into a pre-allocated buffer 
        public MatcherData? Allocate(ReadOnlySpan<char> haystack, int needleLen) {
            int cells = haystack.Length * needleLen;
            if (cells > MAX_MATRIX_LEN || haystack.Length > ushort.MaxValue || needleLen > MAX_NEEDLE_LEN) {
                return null;
            }
            
            nint currentOffset = 0;

            var haystackMemory = AllocateArray<char>(buffer, haystack.Length, ref currentOffset);
            var bonusMemory = AllocateArray<byte>(buffer, haystack.Length, ref currentOffset);
            var rowsMemory = AllocateArray<ushort>(buffer, needleLen, ref currentOffset);
            var cellsMemory = AllocateArray<ScoreCell>(buffer, haystack.Length + 1 - needleLen, ref currentOffset);
            var matrixMemory = AllocateArray<byte>(buffer, (haystack.Length + 1 - needleLen) * needleLen, ref currentOffset);

            if (currentOffset > (long)MatcherData.MAX_SIZE) {
                // Not enough space
                return null;
            }
            
            // Copy haystack over
            haystack.CopyTo(haystackMemory.Span);

            return new MatcherData(haystackMemory, bonusMemory, rowsMemory, cellsMemory, matrixMemory);

            static Memory<T> AllocateArray<T>(void* data, int length, ref nint currentOffset) where T : unmanaged {
                currentOffset = UnsafeExtensions.AlignForward<char>(currentOffset);
                int offset = (int)currentOffset;
                currentOffset += sizeof(T) * length;

                return UnsafeExtensions.AsMemory((T*)Unsafe.Add<byte>(data, offset), length);
            }
        }
    }

    #region Configuration
    
    /// Whether matching should ignore letter casing
    public bool IgnoreCase = false;
    
    #endregion
    
    private readonly Matrix Slab = new();
    
    ~FuzzyMatcher() => Dispose();
    public void Dispose() {
        Slab.Dispose();
        GC.SuppressFinalize(this);
    }

    /// Matches the needle against the haystack and provides a score ranking similarity
    public ushort? GetMatch(ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle) {
        ushort score = MatchFuzzy(needle, haystack, indices: null);
        if (score == NO_MATCH) {
            return null;
        }

        return score;
    }
    /// Matches the needle against the haystack and provides a score ranking similarity
    /// Provides the indices of the matched characters.
    /// This should only be used for already high-matching candidates
    public ushort? GetIndices(ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle, List<int> indices) {
        ushort score = MatchFuzzy(needle, haystack, indices);
        if (score == NO_MATCH) {
            return null;
        }

        return score;
    }
    
    #region Scoring
    
    private enum CharClass { Whitespace, NonWord, Delimiter, Lower, Upper, Number }

    private const ushort NO_MATCH = ushort.MaxValue;

    private const ushort SCORE_MATCH = 16;
    private const ushort PENALTY_GAP_START = 3;
    private const ushort PENALTY_GAP_EXTENSION = 3;
    
    /// We prefer matches at the beginning of a word, but the bonus should not be
    /// too great to prevent the longer acronym matches from always winning over
    /// shorter fuzzy matches. The bonus point here was specifically chosen that
    /// the bonus is cancelled when the gap between the acronyms grows over
    /// 8 characters, which is approximately the average length of the words found
    /// in web2 dictionary and my file system.
    private const ushort BONUS_BOUNDARY = SCORE_MATCH / 2;
    private const ushort BONUS_BOUNDARY_WHITESPACE = BONUS_BOUNDARY + 2;
    private const ushort BONUS_BOUNDARY_DELIMITER = BONUS_BOUNDARY + 1;
    
    /// Edge-triggered bonus for matches in camelCase words.
    /// Their value should be BONUS_BOUNDARY - PENALTY_GAP_EXTENSION = 7.
    /// However, this priporitzes camel case over non-camel case.
    /// In fzf/skim this is not a problem since they score off the max
    /// consecutive bonus. However, we don't do that (because its incorrect)
    /// so to avoids prioritizing camel we use a lower bonus. I think that's fine
    /// usually camel case is wekaer boundary than actual wourd boundaries anyway
    /// This also has the nice sideeffect of perfectly balancing out
    /// camel case, snake case and the consecutive version of the word
    private const ushort BONUS_CAMEL123 = BONUS_BOUNDARY - PENALTY_GAP_START;

    /// Although bonus point for non-word characters is non-contextual, we need it
    /// for computing bonus points for consecutive chunks starting with a non-word
    /// character.
    private const ushort BONUS_NON_WORD = BONUS_BOUNDARY;
    
    // Minimum bonus point given to characters in consecutive chunks.
    // Note that bonus points for consecutive matches shouldn't have needed if we
    // used fixed match score as in the original algorithm.
    private const ushort BONUS_CONSECUTIVE = PENALTY_GAP_START + PENALTY_GAP_EXTENSION;
    
    /// The first character in the typed pattern usually has more significance
    /// than the rest so it's important that it appears at special positions where
    /// bonus points are given, e.g. "to-go" vs. "ongoing" on "og" or on "ogo".
    /// The amount of the extra bonus should be limited so that the gap penalty is
    /// still respected.
    private const ushort BONUS_FIRST_CHAR_MULTIPLIER = 2;

    private ushort MatchFuzzy(ReadOnlySpan<char> needle, ReadOnlySpan<char> haystack, List<int>? indices) {
        if (needle.Length > haystack.Length) {
            return NO_MATCH;
        }
        if (needle.Length == 0) {
            return 0;
        }
        
        if (needle.Length == haystack.Length) {
            return MatchExact(needle, haystack, 0, haystack.Length, indices);
        }
        if (needle.Length == 1) {
            return MatchSubstring1(needle[0], haystack, indices);
        }

        var filter = Prefilter(needle, haystack, onlyGreedy: false);
        if (filter == null) {
            return NO_MATCH;
        }

        (int start, int greedyEnd, int end) = filter.Value;
        if (needle.Length == end - start) {
            return CalculateScore(needle, haystack, start, greedyEnd, indices);
        }
        
        return MatchOptimal(needle, haystack, start, greedyEnd, end, indices);
    }
    private ushort MatchExact(ReadOnlySpan<char> needle, ReadOnlySpan<char> haystack, int start, int end, List<int>? indices) {
        if (needle.Length != end - start) {
            return NO_MATCH;
        }

        bool matched = haystack[start..end].Equals(needle, IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (!matched) {
            return NO_MATCH;
        }

        return CalculateScore(needle, haystack, start, end, indices);
    }
    private ushort MatchSubstring1(char needle, ReadOnlySpan<char> haystack, List<int>? indices) {
        int maxPos = 0;
        ushort maxScore = 0;

        if (IgnoreCase) {
            foreach (int idx in haystack.IndicesOfAny([char.ToLower(needle), char.ToUpper(needle)])) {
                var prevClass = idx > 0 ? GetCharClass(haystack[idx - 1]) : CharClass.Whitespace;
                var currClass = GetCharClass(haystack[idx]);

                ushort bonus = GetClassBonus(prevClass, currClass);
                ushort score = (ushort)(SCORE_MATCH + bonus * BONUS_FIRST_CHAR_MULTIPLIER);

                if (score > maxScore) {
                    maxPos = idx;
                    maxScore = score;
                    
                    // Can't get better than this
                    if (bonus >= BONUS_BOUNDARY_WHITESPACE) {
                        break;
                    }
                }
            }
        } else {
            foreach (int idx in haystack.IndicesOf(needle)) {
                var prevClass = idx > 0 ? GetCharClass(haystack[idx - 1]) : CharClass.Whitespace;
                var currClass = GetCharClass(haystack[idx]);

                ushort bonus = GetClassBonus(prevClass, currClass);
                ushort score = (ushort)(SCORE_MATCH + bonus * BONUS_FIRST_CHAR_MULTIPLIER);

                if (score > maxScore) {
                    maxPos = idx;
                    maxScore = score;
                    
                    // Can't get better than this
                    if (bonus >= BONUS_BOUNDARY_WHITESPACE) {
                        break;
                    }
                }
            }
        }

        if (maxScore == 0) {
            return NO_MATCH;
        }
        
        indices?.Add(maxPos);
        return maxScore;
    }
    private ushort MatchOptimal(ReadOnlySpan<char> needle, ReadOnlySpan<char> haystack, int start, int greedyEnd, int end, List<int>? indices) {
        if (Slab.Allocate(haystack[start..end], needle.Length) is not { } matrix) {
            return MatchGreedy(needle, haystack, start, greedyEnd, indices);
        }
        
        var prevClass = start > 0 ? GetCharClass(haystack[start - 1]) : CharClass.Whitespace;
        bool matched = matrix.Setup(this, needle, prevClass, indices: indices != null);
        if (!matched) {
            throw new UnreachableException("Non-match should have been caught by prefilter. Maybe `needle` is not normalized?");
        }

        int matrixLen = matrix.Populate(this, needle, indices: indices != null);
        int lastRowOffset = matrix.RowOffset.Span[needle.Length - 1];
        int relativeLastRowOffset = lastRowOffset + 1 - needle.Length;

        int matchEnd = 0;
        var matchScoreCell = Matrix.ScoreCell.Unmatched;
        for (int idx = relativeLastRowOffset; idx < matrix.ScoreCells.Span.Length; idx++) {
            var cell = matrix.ScoreCells.Span[idx];
            if (cell.Score > matchScoreCell.Score) {
                matchEnd = idx - relativeLastRowOffset;
                matchScoreCell = cell;
            }
        }

        if (indices != null) {
            matrix.ReconstructOptimalPath((ushort)matchEnd, matrixLen, start, indices);
        }

        return matchScoreCell.Score;
    }
    private ushort MatchGreedy(ReadOnlySpan<char> needle, ReadOnlySpan<char> haystack, int start, int end, List<int>? indices) {
        char needleChar = GetCharNormalized(needle[0]);
        int needleIdx = 1;

        for (int idx = start; idx < end; idx++) {
            char currChar = GetCharNormalized(haystack[idx]);
            if (currChar == needleChar) {
                if (needleIdx < needle.Length) {
                    needleChar = GetCharNormalized(needle[needleIdx]);
                } else {
                    start += 1;
                    break;
                }
            }
        }

        return CalculateScore(needle, haystack, start, end, indices);
    }

    private (int Start, int GreedyEnd, int End)? Prefilter(ReadOnlySpan<char> needle, ReadOnlySpan<char> haystack, bool onlyGreedy) {
        int start = haystack[..(haystack.Length - needle.Length + 1)].IndexOfChar(needle[0], ignoreCase: IgnoreCase);
        if (start == -1) {
            return null;
        }
        
        int greedyEnd = start + 1;
        var currHaystack = haystack[greedyEnd..];
        
        foreach (char needleChar in needle[1..]) {
            int idx = currHaystack.IndexOfChar(needleChar, ignoreCase: IgnoreCase) + 1;
            if (idx == 0) {
                return null;
            }
            
            greedyEnd += idx;
            currHaystack = currHaystack[idx..];
        }

        if (onlyGreedy) {
            return (start, greedyEnd, greedyEnd);
        }

        int end = greedyEnd + currHaystack.LastIndexOfChar(needle[^1], ignoreCase: IgnoreCase) + 1;
        return (start, greedyEnd, end);
    }
    
    private ushort CalculateScore(ReadOnlySpan<char> needle, ReadOnlySpan<char> haystack, int start, int end, List<int>? indices) {
        indices?.EnsureCapacity(end - start);

        var prevClass = start > 0 ? GetCharClass(haystack[start - 1]) : CharClass.Whitespace;

        bool inGap = false;
        int consecutive = 1;

        indices?.Add(start);

        var currClass = GetCharClass(haystack[start]);
        ushort firstBonus = GetClassBonus(prevClass, currClass);
        ushort score = (ushort)(SCORE_MATCH + firstBonus * BONUS_FIRST_CHAR_MULTIPLIER);

        prevClass = currClass;

        char needleChar = GetCharNormalized(needle.Length > 1 ? needle[1] : needle[0]);
        int needleIdx = 2;

        for (int idx = start + 1; idx < end; idx++) {
            (char currChar, currClass) = GetCharClassNormalized(haystack[idx]);
            if (currChar == needleChar) {
                indices?.Add(idx);

                ushort bonus = GetClassBonus(prevClass, currClass);
                if (consecutive != 0) {
                    if (bonus >= BONUS_BOUNDARY && bonus > firstBonus) {
                        firstBonus = bonus;
                    }

                    bonus = Math.Max(Math.Max(bonus, firstBonus), BONUS_CONSECUTIVE);
                } else {
                    firstBonus = bonus;
                }

                score += (ushort)(SCORE_MATCH + bonus);
                inGap = false;
                consecutive += 1;

                if (needleIdx < needle.Length) {
                    needleChar = GetCharNormalized(needle[needleIdx]);
                    needleIdx += 1;
                }
            } else {
                ushort penalty = inGap ? PENALTY_GAP_EXTENSION : PENALTY_GAP_START;

                score = (ushort)(score >= penalty ? score - penalty : 0);
                inGap = true;
                consecutive = 0;
            }
        }

        return score;
    }

    private char GetCharNormalized(char c) {
        if (IgnoreCase && char.IsUpper(c)) {
            return char.ToLower(c);
        }

        return c;
    }
    private (char, CharClass) GetCharClassNormalized(char c) {
        var charClass = GetCharClass(c);

        if (IgnoreCase && charClass == CharClass.Upper) {
            return (char.ToLower(c), charClass);
        }

        return (c, charClass);
    }
    static CharClass GetCharClass(char c) {
        if (char.IsLower(c)) {
            return CharClass.Lower;
        }
        if (char.IsUpper(c)) {
            return CharClass.Upper;
        }
        if (char.IsDigit(c)) {
            return CharClass.Number;
        }
        if (char.IsWhiteSpace(c)) {
            return CharClass.Whitespace;
        }
        if (c is '/' or ',' or ':' or ';' or '|') {
            return CharClass.Delimiter;
        }

        return CharClass.NonWord;
    }
    static ushort GetClassBonus(CharClass prevClass, CharClass nextClass) {
        if (nextClass > CharClass.Delimiter) {
            // Transition from non-word to word
            switch (prevClass) {
                case CharClass.Whitespace:
                    return BONUS_BOUNDARY_WHITESPACE;
                case CharClass.Delimiter:
                    return BONUS_BOUNDARY_DELIMITER;
                case CharClass.NonWord:
                    return BONUS_BOUNDARY;
            }
        }

        if (prevClass == CharClass.Lower && nextClass == CharClass.Upper ||
            prevClass != CharClass.Number && nextClass == CharClass.Number
        ) {
            // camelCase letter123
            return BONUS_CAMEL123;
        }

        if (nextClass == CharClass.Whitespace) {
            return BONUS_BOUNDARY_WHITESPACE;
        }
        if (nextClass == CharClass.NonWord) {
            return BONUS_NON_WORD;
        }

        return 0;
    }

    #endregion
}
