using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal static class InputCoordinateMapper
{
    public static bool TryMap(
        InputAction action,
        WindowDescriptor targetWindow,
        out InputPoint? screenPoint,
        out string? failureCode,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(targetWindow);

        if (action.Point is null)
        {
            screenPoint = null;
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = "Pointer action не содержит обязательную point.";
            return false;
        }

        if (string.Equals(action.CoordinateSpace, InputCoordinateSpaceValues.Screen, StringComparison.Ordinal))
        {
            return TryMapScreen(action.Point, targetWindow.Bounds, out screenPoint, out failureCode, out reason);
        }

        if (string.Equals(action.CoordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal))
        {
            return TryMapCapturePixels(action.Point, action.CaptureReference, targetWindow, out screenPoint, out failureCode, out reason);
        }

        screenPoint = null;
        failureCode = InputFailureCodeValues.UnsupportedCoordinateSpace;
        reason = $"Runtime не поддерживает coordinateSpace '{action.CoordinateSpace}' для click-first input subset.";
        return false;
    }

    public static bool TryBuildDispatchPlan(
        InputAction action,
        WindowDescriptor targetWindow,
        out InputPointerDispatchPlan? dispatchPlan,
        out string? failureCode,
        out string? reason)
    {
        if (!TryMap(action, targetWindow, out InputPoint? screenPoint, out failureCode, out reason)
            || screenPoint is null)
        {
            dispatchPlan = null;
            return false;
        }

        dispatchPlan = new(action, screenPoint);
        return true;
    }

    public static bool TryValidateDispatchPlan(
        InputPointerDispatchPlan dispatchPlan,
        WindowDescriptor targetWindow,
        out InputPointerDispatchPlan? validatedDispatchPlan,
        out string? failureCode,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(dispatchPlan);
        ArgumentNullException.ThrowIfNull(targetWindow);

        if (string.Equals(dispatchPlan.Action.CoordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal))
        {
            return TryValidateCapturePixelsDispatchPlan(dispatchPlan, targetWindow, out validatedDispatchPlan, out failureCode, out reason);
        }

        validatedDispatchPlan = dispatchPlan;
        if (!ValidateScreenPoint(dispatchPlan.ResolvedScreenPoint, targetWindow.Bounds, out failureCode, out reason))
        {
            validatedDispatchPlan = null;
            return false;
        }

        return true;
    }

    private static bool TryMapScreen(
        InputPoint requestedPoint,
        Bounds liveBounds,
        out InputPoint? screenPoint,
        out string? failureCode,
        out string? reason)
    {
        if (!ContainsPoint(liveBounds, requestedPoint))
        {
            screenPoint = null;
            failureCode = InputFailureCodeValues.PointOutOfBounds;
            reason = "Указанная screen point находится вне текущих live window bounds окна-цели.";
            return false;
        }

        screenPoint = requestedPoint;
        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryMapCapturePixels(
        InputPoint requestedPoint,
        InputCaptureReference? captureReference,
        WindowDescriptor targetWindow,
        out InputPoint? screenPoint,
        out string? failureCode,
        out string? reason)
    {
        if (captureReference?.Bounds is not InputBounds captureBounds)
        {
            screenPoint = null;
            failureCode = InputFailureCodeValues.CaptureReferenceRequired;
            reason = "Coordinate space capture_pixels требует captureReference с bounds.";
            return false;
        }

        Bounds liveBounds = targetWindow.Bounds;
        if (!CaptureReferenceGeometryPolicy.TryCreateGeometryBasis(captureReference, out CaptureReferenceGeometryBasis? basis)
            || basis is null
            || !CaptureReferenceMatchesLiveGeometry(basis, liveBounds, targetWindow.EffectiveDpi))
        {
            screenPoint = null;
            failureCode = InputFailureCodeValues.CaptureReferenceStale;
            reason = "Capture reference больше не совпадает с текущей live geometry окна-цели.";
            return false;
        }

        if (requestedPoint.X < 0
            || requestedPoint.Y < 0
            || requestedPoint.X >= captureReference.PixelWidth
            || requestedPoint.Y >= captureReference.PixelHeight)
        {
            screenPoint = null;
            failureCode = InputFailureCodeValues.PointOutOfBounds;
            reason = "Указанная capture_pixels point выходит за пределы capture raster.";
            return false;
        }

        int originX = liveBounds.Left + basis.ContentOffsetX;
        int originY = liveBounds.Top + basis.ContentOffsetY;

        if (!TryAddCoordinate(originX, requestedPoint.X, out int screenX)
            || !TryAddCoordinate(originY, requestedPoint.Y, out int screenY))
        {
            screenPoint = null;
            failureCode = InputFailureCodeValues.PointOutOfBounds;
            reason = "Указанная capture_pixels point выходит за пределы допустимого screen coordinate range.";
            return false;
        }

        InputPoint candidateScreenPoint = new(screenX, screenY);
        if (!ContainsPoint(liveBounds, candidateScreenPoint))
        {
            screenPoint = null;
            failureCode = InputFailureCodeValues.PointOutOfBounds;
            reason = "Resolved capture_pixels screen point больше не принадлежит текущим live window bounds окна-цели.";
            return false;
        }

        screenPoint = candidateScreenPoint;
        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidateCapturePixelsDispatchPlan(
        InputPointerDispatchPlan dispatchPlan,
        WindowDescriptor targetWindow,
        out InputPointerDispatchPlan? validatedDispatchPlan,
        out string? failureCode,
        out string? reason)
    {
        if (!TryMapCapturePixels(
                dispatchPlan.Action.Point!,
                dispatchPlan.Action.CaptureReference,
                targetWindow,
                out InputPoint? refreshedScreenPoint,
                out failureCode,
                out reason))
        {
            validatedDispatchPlan = null;
            return false;
        }

        validatedDispatchPlan = dispatchPlan with
        {
            ResolvedScreenPoint = refreshedScreenPoint!,
        };
        return true;
    }

    private static bool ValidateScreenPoint(
        InputPoint screenPoint,
        Bounds liveBounds,
        out string? failureCode,
        out string? reason)
    {
        if (!ContainsPoint(liveBounds, screenPoint))
        {
            failureCode = InputFailureCodeValues.PointOutOfBounds;
            reason = "Resolved screen point больше не принадлежит текущим live window bounds окна-цели на dispatch boundary.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool ContainsPoint(Bounds bounds, InputPoint point) =>
        point.X >= bounds.Left
        && point.X < bounds.Right
        && point.Y >= bounds.Top
        && point.Y < bounds.Bottom;

    private static bool CaptureReferenceMatchesLiveGeometry(
        CaptureReferenceGeometryBasis basis,
        Bounds liveBounds,
        int liveEffectiveDpi)
    {
        if (!CaptureReferenceGeometryPolicy.BoundsMatchWithinDrift(basis.FrameBounds, liveBounds))
        {
            return false;
        }

        return ValidateCaptureDpiStillMatches(basis, liveEffectiveDpi);
    }

    private static bool ValidateCaptureDpiStillMatches(
        CaptureReferenceGeometryBasis basis,
        int liveEffectiveDpi) =>
        basis.EffectiveDpi is not int captureDpi || captureDpi == liveEffectiveDpi;

    private static bool TryGetExtent(int startEdge, int endEdge, out long extent)
    {
        extent = (long)endEdge - startEdge;
        return extent > 0;
    }

    private static bool TryAddCoordinate(int origin, int offset, out int coordinate)
    {
        long value = (long)origin + offset;
        if (value is < int.MinValue or > int.MaxValue)
        {
            coordinate = 0;
            return false;
        }

        coordinate = (int)value;
        return true;
    }

}
