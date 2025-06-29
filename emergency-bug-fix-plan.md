# Emergency Bug Fix Plan - HoverTrailer Plugin

## Critical Issues Identified

### 1. **Backwards Conditional Logic (Line 170)**
- **Problem**: `if (localTrailer != null)` leads to error handling instead of trailer processing
- **Impact**: When local trailers ARE found, the plugin returns 404 "Trailer not found"
- **Fix**: Reverse the logic to process trailers when found

### 2. **Missing Remote Trailer Implementation**
- **Problem**: No code to handle `movie.RemoteTrailers` detection
- **Impact**: 500 NullReferenceException when trying to access remote trailers
- **Fix**: Implement complete remote trailer detection logic

### 3. **Broken Control Flow**
- **Problem**: Method doesn't properly return `Ok(trailerInfo)` for successful cases
- **Impact**: Successful trailer detection falls through to error cases
- **Fix**: Restructure method flow with proper early returns

### 4. **Null Reference Issues**
- **Problem**: `trailerInfo` can be null when accessing `.TrailerType` property
- **Impact**: Runtime NullReferenceException
- **Fix**: Ensure `trailerInfo` is properly initialized before use

## Implementation Strategy

### Phase 1: Fix Local Trailer Detection
1. Correct the conditional logic for `localTrailer != null`
2. Create proper `TrailerInfo` object when local trailer found
3. Add early return for successful local trailer detection

### Phase 2: Implement Remote Trailer Detection
1. Add logic to check `movie.RemoteTrailers` when no local trailer found
2. Create `TrailerInfo` object for remote trailers with proper properties
3. Implement URL parsing for trailer source identification

### Phase 3: Error Handling Cleanup
1. Ensure 404 is only returned when NO trailers found (local OR remote)
2. Add comprehensive logging for debugging
3. Test both local and remote trailer scenarios

## Code Structure After Fix

```csharp
public async Task<ActionResult<TrailerInfo>> GetTrailerInfo(Guid movieId)
{
    // Step 1: Local trailer detection
    var localTrailers = movie.GetExtras(new[] { ExtraType.Trailer });
    var localTrailer = localTrailers.FirstOrDefault();

    if (localTrailer != null)
    {
        // Create and return local trailer info
        var localTrailerInfo = new TrailerInfo { ... };
        return Ok(localTrailerInfo);
    }

    // Step 2: Remote trailer detection
    if (movie.RemoteTrailers?.Any() == true)
    {
        // Create and return remote trailer info
        var remoteTrailerInfo = new TrailerInfo { ... };
        return Ok(remoteTrailerInfo);
    }

    // Step 3: No trailers found
    return NotFound(error);
}
```

## Priority: CRITICAL
This fix is required immediately as it completely breaks trailer detection functionality for both local and remote trailers.