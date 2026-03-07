//************************************************
//*  IP Interface DLL 32 Bits functions          *
//*  Library Version 11.1.0.0                    *
//*  Copyright 2009 - 2023 All rigths reserved   *
//*  RECOGNIFORM TECHNOLGIES SPA                 *
//*  www.recogniform.com - info@recogniform.com  *
//************************************************
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

public static class RecoIP
{
        internal const string recoIP = "recoip.dll";

        //  Goal: load an image from file to memory
        //  Requires: file name, page number
        //  Result: image handle
        [DllImport(recoIP, EntryPoint = "ImgOpen")]
        internal static extern int ImgOpen(string FileName, int PageNumber);

        //  Goal: remove an image from the memory
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDelete")]
        internal static extern void ImgDelete(int ImageHandle);

        //  Goal: Deskews the image
        //  Requires: Image Handle, Range of deskewing, Resolution, Step, if background is white, Interpolation
        //  Result: calculated deskew angle
        [DllImport(recoIP, EntryPoint = "ImgDeskew")]
        internal static extern double ImgDeskew(int ImageHandle, int MaxAngle, double Accuracy, int AStep, int FillBlack, int Interpolation);

        //  Goal: Removes the horizontal lines from monochrome images
        //  Requires: Image Handle, minimum length in pixel, max number of interruptions, minimum ratio (length/thickness), clean borders, Reconnect Character
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveHorizontalLines")]
        internal static extern int ImgRemoveHorizontalLines(int ImageHandle, int MinHLen, int MaxHBreaks, double MinHRatio, bool CleanBorders, bool ReconnectCharacters);

        //  Goal:Saves an image in PDF format
        //  Requires: Image Handle, file name
        //  Result: value that specifies if saving operation was correctly executed
        [DllImport(recoIP, EntryPoint = "ImgSaveAsPdf")]
        internal static extern void ImgSaveAsPdf(int ImageHandle, string FileName);

        //  Goal: Save an image in pdf format specifying document information (separated by carriage return) and JPEG compression (if the image is gray/color).
        //  Requires: image handle, File Name, document information (separated by carriage return), JPEG compression, file output format [PDF(False) or PDF/A (True)]
        //  Result:  n/a
        [DllImport(recoIP, EntryPoint = "ImgSaveAsPdfEx")]
        internal static extern void ImgSaveAsPdfEx(int ImageHandle, string FileName, string DocInfo, int JPEGQFactor, bool PDFA);

        //  Goal: It extract the standard DIB handle from an image handle
        //  Requires: image handle
        //  Result: DIB handle. 
        [DllImport(recoIP, EntryPoint = "ImgGetDIBHandle")]
        internal static extern int ImgGetDIBHandle(int ImageHandle);

        //  Goal: Set a DIB handle in an image
        //  Requires: image handle, DIB handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSetDIBHandle")]
        internal static extern void ImgSetDIBHandle(int ImageHandle, int DIBHandle);

        //  Goal:  Crypt/encrypt an image using a specific key
        //  Requires: image handle, key
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgEncrypt")]
        internal static extern void ImgEncrypt(int ImageHandle, int key);

        //  Goal: Save the image in JPEG 2000 format
        //  Requires: HandleImage: image handle, File Name, quantization factor
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSaveAsJ2K")]
        internal static extern void ImgSaveAsJ2K(int ImageHandle, string FileName, int QFactor);
       
        //  Goal: save an image from memory to file in TIF format
        //  Requires: image handle, file name, compression type, rows per strip
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSaveAsTif")]
        internal static extern void ImgSaveAsTif(int ImageHandle, string FileName, int Compression, int RowsPerStrips);

        //  Goal: save an image from memory to file in BMP format
        //  Requires: image handle, file name
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSaveAsBmp")]
        internal static extern void ImgSaveAsBmp(int ImageHandle, string FileName);

        //  Goal: save an image from memory to file in JPEG format
        //  Requires: image handle, file name, compression quality
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSaveAsJpg")]
        internal static extern void ImgSaveAsJpg(int ImageHandle, string FileName, int QFactor);

        //  Goal: save an image from memory to file in PNG format
        //  Requires: image handle, file name
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSaveAsPng")]
        internal static extern void ImgSaveAsPng(int ImageHandle, string FileName);
    
        //  Goal: save an image from memory to file in GIF format
        //  Requires: image handle, file name
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSaveAsGif")]
        internal static extern void ImgSaveAsGif(int ImageHandle, string FileName);
      
        //  Goal: copy EXIF data from an image file to other
        //  Requires: input file name, output file name
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgCopyExifData")]
        internal static extern int ImgCopyExifData(string InputFileName, string OutputFileName); 

        //  Goal: copy a piece of image into a new image
        //  Requires: image handle, area coordinates
        //  Result: new image handle
        [DllImport(recoIP, EntryPoint = "ImgCopy")]
        internal static extern int ImgCopy(int ImageHandle, int Left , int Top, int Right, int Bottom );
    
        //  Goal: duplicate an image
        //  Requires: image handle
        //  Result: new image handle
        [DllImport(recoIP, EntryPoint = "ImgDuplicate")]
        internal static extern int ImgDuplicate(int ImageHandle);
       
        //  Goal: get the red channel from a true color image
        //  Requires: image handle
        //  Result: new grayscale image handle representing the red channel
        [DllImport(recoIP, EntryPoint = "ImgGetRedChannel")]
        internal static extern int ImgGetRedChannel(int ImageHandle);
       
        //  Goal: get the green channel from a true color image
        //  Requires: image handle
        //  Result: new grayscale image handle representing the green channel
        [DllImport(recoIP, EntryPoint = "ImgGetGreenChannel")]
        internal static extern int ImgGetGreenChannel(int ImageHandle);
              
        //  Goal: get the blue channel from a true color image
        //  Requires: image handle
        //  Result: new grayscale image handle representing the blue channel
        [DllImport(recoIP, EntryPoint = "ImgGetBlueChannel")]
        internal static extern int ImgGetBlueChannel(int ImageHandle);
       
        //  Goal: build a true color image from 3 grayscale images representing red, green and blue channels
        //  Requires: red image handle, blue image handle, green image handle
        //  Result: new truecolor image handle
        [DllImport(recoIP, EntryPoint = "ImgMergeRGBChannels")]
        internal static extern int ImgMergeRGBChannels(int ImageHandleRed, int ImageHandleGreen , int ImageHandleBlue);
              
        //  Goal: auto orient the image
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAutoOrient")]
        internal static extern void ImgAutoOrient(int ImageHandle);
              
        //  Goal: auto invert the image
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAutoInvert")]
        internal static extern void ImgAutoInvert(int ImageHandle);
              
        //  Goal: deskew the image in safe way, enlarging it avoiding to loose borders piecese
        //  Requires: image handle, max expected skew angle, Accuracy, Step, black background filling flag, interpolation flag
        //  Result: corrected skew angle
        [DllImport(recoIP, EntryPoint = "ImgDeskewSafe")]
        internal static extern double ImgDeskewSafe(int ImageHandle, int MaxAngle, double Accuracy , int AStep , int FillBlack, int Interpolation);
              
        //  Goal: evaluate the skew angle
        //  Requires: image handle, max expected skew angle, Accuracy, Step,
        //  Result: skew angle
        [DllImport(recoIP, EntryPoint = "ImgEvaluateSkew")]
        internal static extern double ImgEvaluateSkew(int ImageHandle, double MaxAngle, double AngleResolution, int Step);

        //  Goal: Evaluates if an image is well focused.
        //  Requires: image handle
        //  Result: level of image focus
        [DllImport(recoIP, EntryPoint = "ImgEvaluateFocus")]
        internal static extern double ImgEvaluateFocus(int ImageHandle);

        //  Goal: evaluate the orientation
        //  Requires: image handle
        //  Result: orientation angle
        [DllImport(recoIP, EntryPoint = "ImgEvaluateOrientation")]
        internal static extern double ImgEvaluateOrientation(int ImageHandle ,int TPL, int TUD );
              
        //  Goal: evaluate the inversion
        //  Requires: image handle
        //  Result: inversion status
        [DllImport(recoIP, EntryPoint = "ImgEvaluateInversion")]
        internal static extern int ImgEvaluateInversion(int ImageHandle);
              
        //  Goal: evaluate the brightness
        //  Requires: image handle
        //  Result: brightness
        [DllImport(recoIP, EntryPoint = "ImgEvaluateBrightness")]
        internal static extern double ImgEvaluateBrightness(int ImageHandle);
              
        //  Goal: evaluate the contrast
        //  Requires: image handle
        //  Result: contrast
        [DllImport(recoIP, EntryPoint = "ImgEvaluateContrast")]
        internal static extern double ImgEvaluateContrast(int ImageHandle);
              
        //  Goal: evaluate the variance
        //  Requires: image handle
        //  Result: variance
        [DllImport(recoIP, EntryPoint = "ImgEvaluateVariance")]
        internal static extern double ImgEvaluateVariance(int ImageHandle);
              
        //  Goal: evaluate the chromaticity
        //  Requires: image handle, minimum pixel saturation to be considered colored, minimum percent coverage of pixels
        //  Result: chromaticity
        [DllImport(recoIP, EntryPoint = "ImgEvaluateChromaticity")]
        internal static extern int ImgEvaluateChromaticity(int ImageHandle, double MinSaturation, double MinCoveragePercent);
              
        //  Goal: open a QC session
        //  Requires: n/a
        //  Result: QC handle
        [DllImport(recoIP, EntryPoint = "ImgQualityControlInitialize")]
        internal static extern int ImgQualityControlInitialize();
              
        //  Goal: close a QC session
        //  Requires: QC handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgQualityControlFinalize")]
        internal static extern void ImgQualityControlFinalize(int QCHandle);
              
        //  Goal: retrieve the name of QC measure
        //  Requires: QC handle, measure id, measure index, buffer for measure name output
        //  Result: measure name length
        [DllImport(recoIP, EntryPoint = "ImgQualityControlGetMeasureName")]
        internal static extern int ImgQualityControlGetMeasureName(int QCHandle, int Measure, int Index, string MeasureName);
              
        //  Goal: retrieve a QC measure
        //  Requires: QC handle, measure id, measure index
        //  Result: measure value
        [DllImport(recoIP, EntryPoint = "ImgQualityControlGetMeasure")]
        internal static extern int ImgQualityControlGetMeasure(int QCHandle, int Measure , int Index);
              
        //  Goal: set a QC parameter
        //  Requires: QC handle, parameter index, parameter value
        //  Result: success
        [DllImport(recoIP, EntryPoint = "ImgQualityControlSetParameter")]
        internal static extern bool ImgQualityControlSetParameter(int QCHandle, int ParameterIndex, int ParameterValue);
              
        //  Goal: execute the QC
        //  Requires: QC handle, front image handle, back image handle, file name
        //  Result: success
        [DllImport(recoIP, EntryPoint = "ImgQualityControlExecute")]
        internal static extern bool ImgQualityControlExecute(int QCHandle, int FrontImageHandle, int BackImageHandle , string FileName);
              
        //  Goal: correct the deformation
        //  Requires: image handle, horz skew angle, vert skew angle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgCorrectDeformation")]
        internal static extern void ImgCorrectDeformation(int ImageHandle, double HAngle, double VHandle);

        [DllImport(recoIP, EntryPoint = "ImgCorrectDeformation")]
        internal static extern void ImgCorrectDeformation1(int ImageHandle, double HAngle, double VHandle, bool Whitebackground );

    //  Goal: despeckle grayscale image
    //  Requires: image handle, iterations
    //  Result: n/a
    [DllImport(recoIP, EntryPoint = "ImgGrayDespeckle")]
        internal static extern void ImgGrayDespeckle(int ImageHandle, int Iterations);
              
        //  Goal: despeckle grayscale image
        //  Requires: image handle, iterations
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDespeckleGray")]
        internal static extern void ImgDespeckleGray(int ImageHandle, int Iterations );
              
        //  Goal: despeckle bitonal image
        //  Requires: image handle, max speckle width, max specke height
        //  Result: pixels removed
        [DllImport(recoIP, EntryPoint = "ImgDespeckle")]
        internal static extern int ImgDespeckle(int ImageHandle, int MaxWidth, int MaxHeight);
              
        //  Goal: despeckle bitonal image in smart way
        //  Requires: image handle, max speckle width, max specke height
        //  Result: pixels removed
        [DllImport(recoIP, EntryPoint = "ImgDespeckleSmart")]
        internal static extern int ImgDespeckleSmart(int ImageHandle , int MaxWidth , int MaxHeight ); 
              
        //  Goal: despeckle bitonal image using zones
        //  Requires: image handle, max speckle width, max specke height, min points, zone size
        //  Result: pixels removed
        [DllImport(recoIP, EntryPoint = "ImgDespeckleZonal")]
        internal static extern int ImgDespeckleZonal(int ImageHandle, int MaxWidth , int MaxHeight, int MinPoints , int ZonesSize );
              
        //  Goal: crop border
        //  Requires: image handle, left, top, right, bottom
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgCropBorder")]
        internal static extern void ImgCropBorder(int ImageHandle, int Left , int Top , int Right , int Bottom);
              
        //  Goal: Execute a trapezoid image crop
        //  Requires: image handle, left and top coordinates of each trapezoid angle point, background color
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgCropTrapezoid")]
        internal static extern void ImgCropTrapezoid(int ImageHandle, int LeftCorner1, int TopCorner1, int LeftCorner2, int TopCorner2,int LeftCorner3, int TopCorner3,int LeftCorner4, int TopCorner4, int ColorBack);

        //  Goal: clean border
        //  Requires: image handle, left, top, right, bottom
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgCleanBorder")]
        internal static extern void ImgCleanBorder(int ImageHandle, int Left, int Top , int Right , int Bottom );
              
        //  Goal: auto clean border
        //  Requires: image handle, max border size, max holes
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAutoCleanBorder")]
        internal static extern void ImgAutoCleanBorder(int ImageHandle , double MaxBorderSize , int MaxHoles );
              
        //  Goal: find black border left
        //  Requires: image handle, min black percent, max holes
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindBlackBorderLeft")]
        internal static extern int ImgFindBlackBorderLeft(int ImageHandle , double MinBlackPercent , int MaxHoles); 
              
        //  Goal: find black border top
        //  Requires: image handle, min black percent, max holes
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindBlackBorderTop")]
        internal static extern int ImgFindBlackBorderTop(int ImageHandle, double MinBlackPercent, int MaxHoles); 
              
        //  Goal: find black border right
        //  Requires: image handle, min black percent, max holes
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindBlackBorderRight")]
        internal static extern int ImgFindBlackBorderRight(int ImageHandle, double MinBlackPercent , int MaxHoles ); 
              
        //  Goal: find black border bottom
        //  Requires: image handle, min black percent, max holes
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindBlackBorderBottom")]
        internal static extern int ImgFindBlackBorderBottom(int ImageHandle, double MinBlackPercent, int MaxHoles);
              
        //  Goal: find change of color in left border
        //  Requires: image handle, min change percent, max border size, dark to light flag
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindBorderChangeLeft")]
        internal static extern int ImgFindBorderChangeLeft(int ImageHandle, double MinChangePercent , int MaxBorderSize , bool DarkToLight); 
              
        //  Goal: find change of color in top border
        //  Requires: image handle, min change percent, max border size, dark to light flag
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindBorderChangeTop")]
        internal static extern int ImgFindBorderChangeTop(int ImageHandle, double MinChangePercent, int MaxBorderSize , bool DarkToLight); 
              
        //  Goal: find change of color in right border
        //  Requires: image handle, min change percent, max border size, dark to light flag
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindBorderChangeRight")]
        internal static extern int ImgFindBorderChangeRight(int ImageHandle , double MinChangePercent , int MaxBorderSize , bool DarkToLight);
              
        //  Goal: find change of color in bottom border
        //  Requires: image handle, min change percent, max border size, dark to light flag
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindBorderChangeBottom")]
        internal static extern int ImgFindBorderChangeBottom(int ImageHandle , double MinChangePercent , int MaxBorderSize , bool DarkToLight);
              
        //  Goal: find border line left
        //  Requires: image handle, max border size, min line percentage
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindBorderLineLeft")]
        internal static extern int ImgFindBorderLineLeft(int ImageHandle , int MaxBorderSize , double MinLinePercent);
              
        //  Goal: find border line top
        //  Requires: image handle, max border size, min line percentage
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindBorderLineTop")]
        internal static extern int ImgFindBorderLineTop(int ImageHandle, int MaxBorderSize, double MinLinePercent);
              
        //  Goal: find border line right
        //  Requires: image handle, max border size, min line percentage
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindBorderLineRight")]
        internal static extern int ImgFindBorderLineRight(int ImageHandle, int MaxBorderSize , double MinLinePercent );
              
        //  Goal: find border line bottom
        //  Requires: image handle, max border size, min line percentage
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindBorderLineBottom")]
        internal static extern int ImgFindBorderLineBottom(int ImageHandle , int MaxBorderSize , double MinLinePercent );
              
        //  Goal: find skew black border left
        //  Requires: image handle, max border size, min line percentage
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindSkewBlackBorderLeft")]
        internal static extern int ImgFindSkewBlackBorderLeft(int ImageHandle , double MaxHandle , bool Noisy ); 
              
        //  Goal: find skew black border top
        //  Requires: image handle, max border size, min line percentage
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindSkewBlackBorderTop")]
        internal static extern int ImgFindSkewBlackBorderTop(int ImageHandle , double MaxHandle , bool Noisy ); 
              
        //  Goal: find skew black border right
        //  Requires: image handle, max border size, min line percentage
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindSkewBlackBorderRight")]
        internal static extern int ImgFindSkewBlackBorderRight(int ImageHandle , double MaxHandle , bool Noisy ); 
              
        //  Goal: find skew black border bottom
        //  Requires: image handle, max border size, min line percentage
        //  Result: border coordinate
        [DllImport(recoIP, EntryPoint = "ImgFindSkewBlackBorderBottom")]
        internal static extern int ImgFindSkewBlackBorderBottom(int ImageHandle , double MaxHandle , bool Noisy ); 
              
        //  Goal: threshold the image
        //  Requires: image handle, threshold level
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgThreshold")]
        internal static extern void ImgThreshold(int ImageHandle , int Threshold );

        //  Goal: Apply dynamic threshold using algorithm based on clustering of background and foreground pixels
        //  Requires: image handle, width window, height windows
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgFBCThreshold")]
        internal static extern void ImgFBCThreshold(int ImageHandle , int width, int height ); 

        //  Goal: auto-threshold the image
        //  Requires: image handle, threshold algo
        //  Result: threshold used
        [DllImport(recoIP, EntryPoint = "ImgAutoThreshold")]
        internal static extern int ImgAutoThreshold(int ImageHandle , int ThresholdAlgo);

        //  Goal: Applies advanced thresholding to the image using standard deviation algo.
        //  Requires: image handle, window width, window height, Contrast, Brightness
        //  Result: 
        [DllImport(recoIP, EntryPoint = "ImgAdvancedThresholdDeviation")]     
        internal static extern void ImgAdvancedThresholdDeviation(int ImageHandle , int WindowWidth , int WindowHeight , int Contrast , int Brightness );

        //  Goal: Applies advanced thresholding to the image using variance algo.
        //  Requires: image handle, window width, window height, Contrast, Brightness 
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAdvancedThresholdVariance")] 
        internal static extern void ImgAdvancedThresholdVariance(int ImageHandle , int WindowWidth , int WindowHeight , int Contrast , int Brightness );

        //  Goal: dynamic threshold min/max
        //  Requires: image handle, window width, window height, Controst, Brightness
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDynamicThresholdMinMax")]
        internal static extern void ImgDynamicThresholdMinMax(int ImageHandle , int WindowWidth , int WindowHeight , int Contrast , int Brightness );
              
        //  Goal: dynamic threshold average
        //  Requires: image handle, window width, window height, Controst, Brightness
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDynamicThresholdAverage")]
        internal static extern void ImgDynamicThresholdAverage(int ImageHandle , int WindowWidth , int WindowHeight , int Contrast , int Brightness );
              
        //  Goal: dynamic threshold Deviation
        //  Requires: image handle, window width, window height, Controst, Brightness
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDynamicThresholdDeviation")]
        internal static extern void ImgDynamicThresholdDeviation(int ImageHandle , int WindowWidth , int WindowHeight , int Contrast , int Brightness );
              
        //  Goal: dynamic threshold Variance
        //  Requires: image handle, window width, window height, Controst, Brightness
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDynamicThresholdVariance")]
        internal static extern void ImgDynamicThresholdVariance(int ImageHandle , int WindowWidth , int WindowHeight , int Contrast , int Brightness );
              
        //  Goal: edge threshold
        //  Requires: image handle, window width, window height, Controst, Max noise size
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgEdgeThreshold")]
        internal static extern void ImgEdgeThreshold(int ImageHandle , int WindowWidth , int WindowHeight , int Contrast , int MaxSpotSize );
              
        //  Goal: adaptive threshold min/max
        //  Requires: image handle, window width, window height, Controst, Brightness
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAdaptiveThresholdMinMax")]
        internal static extern void ImgAdaptiveThresholdMinMax(int ImageHandle , int WindowWidth , int WindowHeight , int Contrast , int Brightness );
              
        //  Goal: adaptive threshold average
        //  Requires: image handle, window width, window height, Controst, Brightness
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAdaptiveThresholdAverage")]
        internal static extern void ImgAdaptiveThresholdAverage(int ImageHandle , int WindowWidth , int WindowHeight , int Contrast , int Brightness );
              
         //  Goal: adaptive threshold backtrack
         //  Requires: image handle, window width, window height, Controst, Brightness
         //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAdaptiveThresholdBackTrack")]
        internal static extern void ImgAdaptiveThresholdBackTrack(int ImageHandle , int WindowWidth , int WindowHeight , int Contrast , int Brightness );
              
         //  Goal: backtrack threshold min/max
         //  Requires: image handle, min threshold, max threshold, contribute
         //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgBackTrackThresholdMinMax")]
        internal static extern void ImgBackTrackThresholdMinMax(int ImageHandle , int MinTreshold , int MaxTreshold , double Contribue );
              
         //  Goal: backtrack threshold average
         //  Requires: image handle, window width, window height, Controst, Brightness
         //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgBackTrackThresholdAverage")]
        internal static extern void ImgBackTrackThresholdAverage(int ImageHandle , int MinTreshold , int MaxTreshold , double Contribue );
              
        //  Goal: refine threshold
        //  Requires: image handle, original grayscale image handle, tolerance
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRefineThreshold")]
        internal static extern void ImgRefineThreshold(int ImageHandle , int OriginalGrayScaleImageHandle , int Tolerance );
              
        //  Goal: PEI threshold
        //  Requires: image handle, parameters
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgPEIThreshold")]
        internal static extern void ImgPEIThreshold(int ImageHandle , string Parameters );
              
        //  Goal: bleed through removal
        //  Requires: image handle, tolerance
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveBleedThrough")]
        internal static extern void ImgRemoveBleedThrough(int ImageHandle , int Tolerace );

        //  Goal: swap red and blue channels
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSwapRedAndBlue")]
        internal static extern void ImgSwapRedAndBlue(int ImageHandle );
              
        //  Goal: remove punch holes
        //  Requires: image handle, sides, style, border size, diameter
        //  Result: number of holes removed
        [DllImport(recoIP, EntryPoint = "ImgRemovePunchHoles")]
        internal static extern int ImgRemovePunchHoles(int ImageHandle , int Sides , int Style , double Border , double Diameter );

        //  Goal: Creates a new empty image
        //  Requires: image width, image height, bits per pixel(1, 4, 8, 24), Resolution.
        //  Result: image handle
        [DllImport(recoIP, EntryPoint = "ImgCreate")]
        internal static extern int ImgCreate(int Width, int Height, int BitPerPixel, int Resolution); 
            
        //  Goal: rotate the image
        //  Requires: image handle, angle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRotate")]
        internal static extern void ImgRotate(int ImageHandle , int Angle );
              
        //  Goal: shift the image
        //  Requires: image handle, horz shift, vert shift
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgShift")]
        internal static extern void ImgShift(int ImageHandle , int HorzShift , int VertShift );
              
        //  Goal: invert the image
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgInvert")]
        internal static extern void ImgInvert(int ImageHandle );
              
        //  Goal: mirror the image
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgMirror")]
        internal static extern void ImgMirror(int ImageHandle );
              
        //  Goal: flip the image
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgFlip")]
        internal static extern void ImgFlip(int ImageHandle );
              
        //  Goal: scale to gray the image
        //  Requires: image handle, zoom factor
        //  Result: gryascale image handle
        [DllImport(recoIP, EntryPoint = "ImgScaleToGray")]
        internal static extern int ImgScaleToGray(int ImageHandle , double Zoom ); 
              
        //  Goal: dither with Bayer algo
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDitherBayer")]
        internal static extern void ImgDitherBayer(int ImageHandle );
              
        //  Goal: dither with Error diffusion algo
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDitherErrorDiffusion")]
        internal static extern void ImgDitherErrorDiffusion(int ImageHandle );
              
        //  Goal: add other image using or operator
        //  Requires: destination image handle, source image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAddOr")]
        internal static extern void ImgAddOr(int DestImageHandle , int SourceImageHandle );
              
        //  Goal: add other image using xor operator
        //  Requires: destination image handle, source image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAddXor")]
        internal static extern void ImgAddXor(int DestImageHandle , int SourceImageHandle );
              
        //  Goal: add other image using and operator
        //  Requires: destination image handle, source image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAddAnd")]
        internal static extern void ImgAddAnd(int DestImageHandle , int SourceImageHandle );
              
        //  Goal: add other image using sum operator
        //  Requires: destination image handle, source image handle, normalizer
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAddSum")]
        internal static extern void ImgAddSum(int DestImageHandle , int SourceImageHandle , int Normalizer );
              
        //  Goal: add other image using sub operator
        //  Requires: destination image handle, source image handle, normalizer
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAddSub")]
        internal static extern void ImgAddSub(int DestImageHandle , int SourceImageHandle , int Normalizer );
              
        //  Goal: add other image using mul operator
        //  Requires: destination image handle, source image handle, normalizer
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAddMul")]
        internal static extern void ImgAddMul(int DestImageHandle , int SourceImageHandle , int Normalizer );
              
        //  Goal: add other image using div operator
        //  Requires: destination image handle, source image handle, normalizer
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAddDiv")]
        internal static extern void ImgAddDiv(int DestImageHandle , int SourceImageHandle , int Normalizer );
              
        //  Goal: stretch the contrast
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgStretchContrast")]
        internal static extern void ImgStretchContrast(int ImageHandle );
              
        //  Goal: equalize the contrast
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgEqualizeContrast")]
        internal static extern void ImgEqualizeContrast(int ImageHandle );
              
        //  Goal: adjust the contrast
        //  Requires: image handle, contrast
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAdjustContrast")]
        internal static extern void ImgAdjustContrast(int ImageHandle, double Value);
              
        //  Goal: adjust the brightness
        //  Requires: image handle, brightness
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAdjustBrightness")]
        internal static extern void ImgAdjustBrightness(int ImageHandle, double Value);
              
        //  Goal: adjust the gamma
        //  Requires: image handle, gamma
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAdjustGamma")]
        internal static extern void ImgAdjustGamma(int ImageHandle, double Value);
              
        //  Goal: adjust the gray levels
        //  Requires: image handle, levels
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAdjustLevels")]
        internal static extern void ImgAdjustLevels(int ImageHandle , int Value );
              
        //  Goal: smooth
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSmooth")]
        internal static extern void ImgSmooth(int ImageHandle);
              
        //  Goal: dilate
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDilate")]
        internal static extern void ImgDilate(int ImageHandle);
              
        //  Goal: erode
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgErode")]
        internal static extern void ImgErode(int ImageHandle);
              
        //  Goal: thin
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgThin")]
        internal static extern void ImgThin(int ImageHandle);
              
        //  Goal: thick
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgThick")]
        internal static extern void ImgThick(int ImageHandle);
              
        //  Goal: resize the image
        //  Requires: image handle, new width, new height, color for new background
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgResize")]
        internal static extern void ImgResize(int ImageHandle , int NewWidth , int NewHeight , int NewBackground );
              
        //  Goal: scale the image
        //  Requires: image handle, new width, new height, interpolation flag
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgScale")]
        internal static extern void ImgScale(int ImageHandle , int NewWidth , int NewHeight , bool Interpolation );
              
        //  Goal: adjust inverted text rects
        //  Requires: image handle, min rects width, max rect height
        //  Result: number of rects adjusted
        [DllImport(recoIP, EntryPoint = "ImgAdjustInvertedTextRects")]
        internal static extern int ImgAdjustInvertedTextRects(int ImageHandle , double MinWidth , double MinHeight ); 
              
        //  Goal: find inverted text rects
        //  Requires: image handle, min rects width, max rect height
        //  Result: rects list handle
        [DllImport(recoIP, EntryPoint = "ImgFindInvertedTextRects")]
        internal static extern int ImgFindInvertedTextRects(int ImageHandle , double MinWidth , double MinHeight );

        //  Goal: Find the cutting line between two book pages looking for dark line
        //  Requires:handle image, tolerance zone, area left coordinate, area top coordinate, area right coordinate, area bottom coordinate,line top coordinate, linebottom coordinate.
        //  Result:n/a 
        [DllImport(recoIP, EntryPoint = "ImgFindBindingByLine")]
        internal static extern void ImgFindBindingByLine(int ImageHandle, int MaxNoise, int Left, int Top, int Right, int Bottom, ref int TopX, ref int BottomX);

        //  Goal: Find the cutting line between two book pages looking for valley generated from book page wave effect.
        //  Requires:handle image, tolerance zone, area left coordinate, area top coordinate, area right coordinate, area bottom coordinate,line top coordinate, linebottom coordinate.
        //  Result:n/a      
        [DllImport(recoIP, EntryPoint = "ImgFindBindingByValleys")]
        internal static extern void ImgFindBindingByValleys(int ImageHandle, int MaxNoise, int Left, int Top, int Right, int Bottom, ref int TopX, ref int BottomX);
              
        //  Goal: count the number of rects in a list
        //  Requires: rects list handle
        //  Result: number of rects
        [DllImport(recoIP, EntryPoint = "ImgRectsCount")]
        internal static extern int ImgRectsCount(int ListHandle ); 
              
        //  Goal: get the left coordinate of a rects in a list
        //  Requires: rects list handle, rect index
        //  Result: coordinate
        [DllImport(recoIP, EntryPoint = "ImgRectGetLeft")]
        internal static extern int ImgRectGetLeft(int ListHandle , int Index ); 
              
        //  Goal: get the top coordinate of a rects in a list
        //  Requires: rects list handle, rect index
        //  Result: coordinate
        [DllImport(recoIP, EntryPoint = "ImgRectGetTop")]
        internal static extern int ImgRectGetTop(int ListHandle , int Index ); 
              
        //  Goal: get the right coordinate of a rects in a list
        //  Requires: rects list handle, rect index
        //  Result: coordinate
        [DllImport(recoIP, EntryPoint = "ImgRectGetRight")]
        internal static extern int ImgRectGetRight(int ListHandle , int Index ); 
              
        //  Goal: get the bottom coordinate of a rects in a list
        //  Requires: rects list handle, rect index
        //  Result: coordinate
        [DllImport(recoIP, EntryPoint = "ImgRectGetBottom")]
        internal static extern int ImgRectGetBottom(int ListHandle , int Index ); 
              
        //  Goal: free a rects list
        //  Requires: rects list handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRectsFree")]
        internal static extern void ImgRectsFree(int ListHandle );
              
        //  Goal: drop color RGB
        //  Requires: image handle, red flag, green flag, blue flag, threshold
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDropColorRGB")]
        internal static extern void ImgDropColorRGB(int ImageHandle , bool Red , bool Green , bool Blue , int Threshold );

        //  Goal: drop component
        //  Requires: image handle, red flag, green flag, blue flag
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDropComponent")]
        internal static extern void ImgDropComponent(int ImageHandle , bool Red , bool Green , bool Blue );
              
        //  Goal: drop color
        //  Requires: image handle, hue/saturation/light start, hue/saturation/light end, hue/saturation/light new color
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDropColorHSL")]
        internal static extern void ImgDropColorHSL(int ImageHandle , double HS , double SS , double LS , double HE , double SE , double LE , double HN , double SN , double LN );
        
        //  Goal: remove vert lines
        //  Requires: image handle, min lenght, max breaks, min ratio, border cleaning flag, characters reconnection flag
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveVerticalLines")]
        internal static extern int ImgRemoveVerticalLines(int ImageHandle , int MinVLen , int MaxVBreaks , double MinVRatio , bool CleanBorders , bool ReconnectCharacters ); 
              
        //  Goal: remove lines
        //  Requires: image handle, min lenght, max breaks, min ratio, border cleaning flag, characters reconnection flag
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveLines")]
        internal static extern int ImgRemoveLines(int ImageHandle , int MinHLen , int MaxHBreaks , double MinHRatio , int MinVLen , int MaxVBreaks , double MinVRatio , bool CleanBorders , bool ReconnectCharacters ); 
              
        //  Goal: remove jpeg artifacts
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveJpegArtifacts")]
        internal static extern void ImgRemoveJpegArtifacts(int ImageHandle );
            
        //  Goal: median filter
        //  Requires: image handle, width, height
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgMedianFilter")]
        internal static extern void ImgMedianFilter(int ImageHandle , int Width , int Height );

        //  Goal: mean filter
        //  Requires: image handle, width, height
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgMeanFilter")]
        internal static extern void ImgMeanFilter(int ImageHandle , int Width , int Height );
              
        //  Goal: high pass
        //  Requires: image handle, radius
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgHighPass")]
        internal static extern void ImgHighPass(int ImageHandle , int Radius );
              
        //  Goal: sharpen
        //  Requires: image handle, radius
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSharpen")]
        internal static extern void ImgSharpen(int ImageHandle , int Radius );
              
        //  Goal: blur
        //  Requires: image handle, radius
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgBlur")]
        internal static extern void ImgBlur(int ImageHandle , int Radius );
              
        //  Goal: apply a polynominial filter
        //  Requires: image handle, width, height, grade
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgPolynominialFilter")]
        internal static extern void ImgPolynominialFilter(int ImageHandle , int Width , int Height , int Grade );
              
        //  Goal: remove black wires
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveBlackWires")]
        internal static extern void ImgRemoveBlackWires(int ImageHandle );
              
        //  Goal: remove white wires
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveWhiteWires")]
        internal static extern void ImgRemoveWhiteWires(int ImageHandle );
             
        //  Goal: remove black wires
        //  Requires: image handle, Threshold, Buffer to receive the name of dominant color
        //  Result: lenght of returned string
        [DllImport(recoIP, EntryPoint = "ImgFindDominantColor")]
        internal static extern int ImgFindDominantColor(int ImageHandle , int Threshold , string DominatColor );

        //  Goal: Evaluate if the image is blank
        //  Requires: image handle, maximum percentage of black pixel permissible
        //  Result: true (image is blank) otherwise false
        [DllImport(recoIP, EntryPoint = "ImgIsBlank")]
        internal static extern bool ImgIsBlank(int ImageHandle, double MaxPercentage); 

        //  Goal: apply matrix convolution 3 x 3
        //  Requires: image handle, weights, divisor, bias
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgConvolve3x3")]
        internal static extern void ImgConvolve3x3(int ImageHandle , int W1 , int W2 , int W3 , int W4 , int W5 , int W6 , int W7 , int W8 , int W9 , int Divisor , int Bias );
              
        //  Goal: apply matrix convolution 5 x 5
        //  Requires: image handle, weights, divisor, bias
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgConvolve5x5")]
        internal static extern void ImgConvolve5x5(int ImageHandle , int W1 , int W2 , int W3 , int W4 , int W5 , int W6 , int W7 , int W8 , int W9 , int W10 , int W11 , int W12 , int W13 , int W14 , int W15 , int W16 , int W17 , int W18 , int W19 , int W20 , int W21 , int W22 , int W23 , int W24 , int W25 , int Divisor , int Bias );
              
        //  Goal: watermark an image
        //  Requires: image handle, maskr image handle, mask image handle, transparence
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgWatermark")]
        internal static extern void ImgWatermark(int ImageHandle , int ImageMaskHandle , int ImageMarkHandle , int Transparence );
              
        //  Goal: apply majority filter
        //  Requires: image handle, width, ehight
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgMajorityFilter")]
        internal static extern void ImgMajorityFilter(int ImageHandle , int Width , int Height );
              
        //  Goal: draw a rectangle
        //  Requires: image handle, x1, y1, x2, y2, color
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDrawLine")]
        internal static extern void ImgDrawLine(int ImageHandle , int X1 , int Y1 , int X2 , int Y2 , int Color );
              
        //  Goal: draw a rectangle
        //  Requires: image handle, x, y, width, height, color, filling flag
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDrawRectangle")]
        internal static extern void ImgDrawRectangle(int ImageHandle , int X , int Y , int W , int H , int Color , bool Filled );
              
        //  Goal: draw an ellipse
        //  Requires: image handle, x, y, width, height, color, filling flag
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDrawEllipse")]
        internal static extern void ImgDrawEllipse(int ImageHandle , int X , int Y , int W , int H , int Color , bool Filled);
              
        //  Goal: draw a barcode
        //  Requires: image handle, x, y, height, modulus width, data
        //  Result: barcode width
        [DllImport(recoIP, EntryPoint = "ImgDrawBarCode")]
        internal static extern int ImgDrawBarCode(int ImageHandle, int X , int Y, int Height , int Modulus , int Data ); 
              
        //  Goal: draw text
        //  Requires: image handle, x, y, font name, font style, orientation, color, data
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgDrawText")]
        internal static extern void ImgDrawText(int ImageHandle , int X , int Y , int FontName , int FontAttributes , int Orientation , int Color , string Data );
              
        //  Goal: add other image using copy operator
        //  Requires: destination image handle, source image handle, x, y
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAddCopy")]
        internal static extern void ImgAddCopy(int DestImageHandle , int SourceImageHandle , int X , int Y );
           
        //  Goal: get width
        //  Requires: image handle
        //  Result: width
        [DllImport(recoIP, EntryPoint = "ImgGetWidth")]
        internal static extern int ImgGetWidth(int ImageHandle ); 
              
        //  Goal: get height
        //  Requires: image handle
        //  Result: height
        [DllImport(recoIP, EntryPoint = "ImgGetHeight")]
        internal static extern int ImgGetHeight(int ImageHandle ); 
              
        //  Goal: get vert resolution
        //  Requires: image handle
        //  Result: resolution
        [DllImport(recoIP, EntryPoint = "ImgGetVertResolution")]
        internal static extern int ImgGetVertResolution(int ImageHandle); 
              
        //  Goal: get horz resolution
        //  Requires: image handle
        //  Result: resolution
        [DllImport(recoIP, EntryPoint = "ImgGetHorzResolution")]
        internal static extern int ImgGetHorzResolution(int ImageHandle ); 
              
        //  Goal: set vert resolution
        //  Requires: image handle, resolution
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSetVertResolution")]
        internal static extern void ImgSetVertResolution(int ImageHandle , int Resolution );
              
        //  Goal: set horz resolution
        //  Requires: image handle, resolution
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSetHorzResolution")]
        internal static extern void ImgSetHorzResolution(int ImageHandle , int Resolution );
      
        //  Goal: get bits per pixel
        //  Requires: image handle
        //  Result: bits depth
        [DllImport(recoIP, EntryPoint = "ImgGetBitsPixel")]
        internal static extern int ImgGetBitsPixel(int ImageHandle ); 
              
        //  Goal: set bits per pixel
        //  Requires: image handle, bits depht
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSetBitsPixel")]
        internal static extern void ImgSetBitsPixel(int ImageHandle , int Depth );
 
        //  Goal: get pixel color
        //  Requires: image handle, x, y
        //  Result: color
        [DllImport(recoIP, EntryPoint = "ImgGetPixel")]
        internal static extern int ImgGetPixel(int ImageHandle , int X , int Y ); 
     
        //  Goal: set pixel color
        //  Requires: image handle, x, y, color
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSetPixel")]
        internal static extern void ImgSetPixel(int ImageHandle , int X , int Y , int C );
   
        //  Goal: get white color
        //  Requires: image handle
        //  Result: color
        [DllImport(recoIP, EntryPoint = "ImgGetWhiteValue")]
        internal static extern int ImgGetWhiteValue(int ImageHandle ); 
              
        //  Goal: get black color
        //  Requires: image handle
        //  Result: color
        [DllImport(recoIP, EntryPoint = "ImgGetBlackValue")]
        internal static extern int ImgGetBlackValue(int ImageHandle ); 
              
        //  Goal: get background color
        //  Requires: image handle
        //  Result: color
        [DllImport(recoIP, EntryPoint = "ImgGetBackgroundColor")]
        internal static extern int ImgGetBackgroundColor(int ImageHandle ); 
   
        //  Goal: get foreground color
        //  Requires: image handle
        //  Result: color
        [DllImport(recoIP, EntryPoint = "ImgGetForegroundColor")]
        internal static extern int ImgGetForegroundColor(int ImageHandle );

        //  Goal: Search image curve
        //  Requires: image handle
        //  Result: Curve handle point
        [DllImport(recoIP, EntryPoint = "ImgWaveDetect")]
        internal static extern int ImgWaveDetect(int ImageHandle );
   
        //  Goal: Correct curve 
        //  Requires: image handle, Curve handle point
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgWaveCorrect")]
        internal static extern void ImgWaveCorrect(int ImageHandle, int PointsHandle );

        //  Goal: Execute curve correction of a book page detecting automatically curve point
        //  Requires: image handle
        //  Result: n/a 
        [DllImport(recoIP, EntryPoint = "ImgWaveCorrection")]
        internal static extern void ImgWaveCorrection(int ImageHandle);
            
        //  Goal: Deallocate curve point
        //  Requires: Curve handle point
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgWaveFree")]
        internal static extern void ImgWaveFree(int PointsHandle );
        
        //  Goal: open image from buffer
        //  Requires: buffer pointer, buffer size, page
        //  Result: image handle
        [DllImport(recoIP, EntryPoint = "ImgOpenFromBuffer")]
        unsafe internal static extern int ImgOpenFromBuffer(void* Buffer, int BufferSize, int Page);

        //  Goal: Return dominant primary color.
        //  Requires: image handle, Color Threshold (0..255), DominantColor
        //  Result:color name
        [DllImport(recoIP, EntryPoint = "ImgGetDominantPrimaryColor")]
        internal static extern int ImgGetDominantPrimaryColor(int ImageHandle, int ColorThreshold, ref StringBuilder DominantColor);

        //  Goal: Return dominant RGB color.
        //  Requires: image handle
        //  Result: RGB color
        [DllImport(recoIP, EntryPoint = "ImgGetDominantRGBColor")]
        internal static extern int ImgGetDominantRGBColor(int HandleImage);

        //  Goal: Create an histogram of gray levels value counting image pixels.
        //  Requires: image handle.
        //  Result: histogram handle
        [DllImport(recoIP, EntryPoint = "ImgHistoCreate")]
        internal static extern int ImgHistoCreate(int ImageHandle);

        //  Goal: Delete an histogram handle.
        //  Requires: image handle.
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgHistoDelete")]
        internal static extern void ImgHistoDelete(int ImageHandle);

        //  Goal: Retrieve the value of histogram for a specified level.
        //  Requires: histogram handle, bin levels
        //  Result:count of pixel into the histogram.
        [DllImport(recoIP, EntryPoint = "ImgHistoValue")]
        internal static extern int ImgHistoValue(int HistoHandle, int Level);
        
        //  Goal: Create PDF image adding hidden searchable text.
        //  Requires:image handle, file name of output file, PDF info,JPEG (0-100) factor, XML text, file output format: PDF(False) or PDF/A (True)
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgMakePdfSearchable")]
        internal static extern void ImgMakePdfSearchable(int andleImage, string FileName, string Info, int JPEG,  string XML, bool PDFA);


        //  Goal:Mask an image using another ones. 
        //  Requires: image handle to mask, mask image handle, horizontal pixel shift, vertical pixel shift, color to use as destination image mask, color used as mask
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgMask")]
        internal static extern void ImgMask(int DestHandleImage, int Mask, int XOffset, int YOffset, int DestColor, int MaskColor);

        //  Goal: Execute image black border removal.
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveBorder")]
        internal static extern void ImgRemoveBorder(int HandleImage);


        //  Goal:Remove the box around chars.
        //  Requires: image handle, minimum box length in pixel, minimum box height in pixel, number of chars in box, reconnect char
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveCharBox")]
        internal static extern void ImgRemoveCharBox(int HandleImage, int MinHorzLen, int MinVertLen, int umOfChar, bool Reconnect);

        //  Goal: Remove box separated around chars.
        //  Requires: image handle, minimum box length, minimum box height, quality box
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveDistCharBox")]
        internal static extern void ImgRemoveDistCharBox(int HandleImage, int MinHorzLen, int MinVertLen, bool PoorQuality);

        //  Goal: Remove box around fields
        //  Requires: image handle, minimum box length, minimum box height, reconnect char.
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveFieldBox")]
        internal static extern void ImgRemoveFieldBox(int HandleImage, int MinHorzLen, int MinVertLen, bool Reconnect);

        //  Goal: Remove image intrusion.
        //  Requires: image handle
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveIntrusions")]
        internal static extern void ImgRemoveIntrusions(int HandleImage);

        //  Goal: This function remove the elements of size bigger than those indicated in parameters.
        //  Requires: image handle, minimum width of element to remove, minimum height of element to remove
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveLargeBlobs")]
        internal static extern void ImgRemoveLargeBlobs(int HandleImage,int MinWidth,int MinHeight);

        //  Goal: This function remove the elements of size smaller than those indicated in parameters.
        //  Requires: image handle, maximum width of element to remove, maximum height of element to remove
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgRemoveSmallBlobs")]
        internal static extern void ImgRemoveSmallBlobs(int HandleImage, int MinWidth, int MinHeight);


        //  Goal: Save image in PDF format adding a second image on transparency.
        //  Requires: image handle, output image file name, overlay image file name.
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSaveAsPdfWithOverlay")]
        internal static extern void ImgSaveAsPdfWithOverlay(int HandleImage, string FileName, string OverlayFileName);


        //  Goal: Save an image as PDF adding hidden searchable text.
        //  Requires: image handle,  output file file name, PDF info, JPEG factor, XML text, file output format: PDF(False) or PDF/A (True)
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgSaveAsPdfSearchable")]
        internal static extern void ImgSaveAsPdfSearchable(int HandleImage,string FileName,string Info, int JPEG,string XML, bool PDFA);

        //Goal: correct the keystone effect
        //Requires: image handle
        //Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgKeystoneCorrection")]
        internal static extern  void ImgKeystoneCorrection (int HandleImage);

        //Goal: balance the white light
        //Requires: image handle
        //Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgWhiteBalance")]
        internal static extern void ImgWhiteBalance (int HandleImage);

        //Goal: count the unique colors
        //Requires: image handles
        //Result: mumber of colors
        [DllImport(recoIP, EntryPoint = "ImgCountUniqueColors")]
        internal static extern long ImgCountUniqueColors (int HandleImage);

        //Goal: convert to palette, 8 bits per pixel
        //Requires: image handle, number of colors
        //Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgConvertToPalette")]
        internal static extern void ImgConvertToPalette (int HandleImage, int Colors);

        //Goal: convert to grayscale, 8 bits per pixel
        //Requires: image handle, conversion method, use red flag, use green flag, use blue flag
        //Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgConvertToGrayScale")]
        internal static extern void ImgConvertToGrayScale (int HandleImage, int Method, bool Red, bool Green, bool Blue);

        //Goal: adjust the background of an image
        //Requires: image handle, background handled
        //Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgAdjustBackground")]
        internal static extern void ImgAdjustBackground (int HandleImage,int HandleBackgroundImage);

        //Goal: estimate the background on an image
        //Requires: image handle
        //Result: image handle
        [DllImport(recoIP, EntryPoint = "ImgEstimateBackground")]
        internal static extern long ImgEstimateBackground (int HandleImage);

        //Goal: detect edges over an image, building a new image with edges draweds
        //Requires: image handle, gaussina theta, lower limit, upper limitd
        //Result: image handle
        [DllImport(recoIP, EntryPoint = "ImgDetectEdges")]
        internal static extern long ImgDetectEdges (int HandleImage, double Theta, double LowerLimit, double UpperLimit);

        //Goal: auto-threshold the image (extra parameters version)
        //Requires: image handle, threshold algo, min threshold, max threshold
        //Result: threshold used
        [DllImport(recoIP, EntryPoint = "ImgAutoThresholdEx")]
        internal static extern long ImgAutoThresholdEx (int HandleImage, int ThresholdAlgo, int MinT, int MaxT);

        //  Goal: Perform multi-threshold
        //  Requires: image handle,  algo, columns, rows, overlap parea percentage, min threshold, max thresholdtext
        //  Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgMultiThreshold")]
        internal static extern void ImgMultiThreshold (int HandleImage, int ThresholdAlgo, int Cols, int Rows, double OverlapPercent, int MinT, int MaxT);

        //Goal: estimate the skew with alternative algo
        //Requires: image handle, max angle, angle resolution, left, top, right bottom ROI
        //Result: skew angle
        [DllImport(recoIP, EntryPoint = "ImgEvaluateSkewAlt")]
        internal static extern double ImgEvaluateSkewAlt (int HandleImage, double MaxAngle, double AngleResolution, long Left, long Top, long Right, long Bottom);

        //Goal: correct thew skew
        //Requires: image handle, angle, interpolation flag, enlargement flag
        //Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgCorrectSkew")]
        internal static extern void ImgCorrectSkew (int HandleImage, double Angle, bool Interpolation, bool Enlarge);

    [DllImport(recoIP, EntryPoint = "ImgCorrectSkew")]
    internal static extern void ImgCorrectSkew1(int HandleImage, double Angle, int IntColor, bool Interpolation, bool Enlarge);


    //Goal: unlock the license used
    //Requires: n/a 
    //Result: 1=license is locked, 0 otherwise
    [DllImport(recoIP, EntryPoint = "ImgUnlockLicense")]
        internal static extern int ImgUnlockLicense();

		//Goal: lock a license 
		//Requires: n/a
		//Result: 1 license available, 0 license unavailable, 2 license in use
        [DllImport(recoIP, EntryPoint = "ImgLockLicense")]
        internal static extern int ImgLockLicense();
		
		//Goal: returns information about dll license
		//Requires: 1 license enabled, 0 otherwise
		//Result: n/a
        [DllImport(recoIP, EntryPoint = "ImgIsLibraryLicensed")]
        internal static extern int ImgIsLibraryLicensed();
}

