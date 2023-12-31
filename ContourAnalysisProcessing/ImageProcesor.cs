﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Emgu.CV;
using Emgu.CV.Structure;
using System.Drawing;
using System.Threading.Tasks;

namespace ContourAnalysis
{
    public class ImageProcessor
    {
        //settings 背景
        public bool equalizeHist = false;
        public bool noiseFilter = false;
        public int cannyThreshold = 50;
        public bool blur = true;
        public int adaptiveThresholdBlockSize = 4;
        public double adaptiveThresholdParameter = 1.2d;
        public bool addCanny = true;
        public bool filterContoursBySize = true;
        public bool onlyFindContours = false;
        public int minContourLength = 15;
        public int minContourArea = 10;
        public double minFormFactor = 0.5;
        //
        public List<Contour<Point>> contours;
        public Templates templates = new Templates();
        public Templates samples = new Templates();
        public List<FoundTemplateDesc> foundTemplates = new List<FoundTemplateDesc>();
        public TemplateFinder finder = new TemplateFinder();
        public Image<Gray, byte> binarizedFrame;


        public void ProcessImage(Image<Bgr, byte> frame)
        {
            ProcessImage(frame.Convert<Gray, Byte>());
        }

        public void ProcessImage(Image<Gray, byte> grayFrame)
        {
            if (equalizeHist)
                grayFrame._EqualizeHist();//autocontrast 自动对比度
            //smoothed 平滑化
            Image<Gray, byte> smoothedGrayFrame = grayFrame.PyrDown();
            smoothedGrayFrame = smoothedGrayFrame.PyrUp();
            //canny 多级边缘检测
            Image<Gray, byte> cannyFrame = null;
            if (noiseFilter)
                cannyFrame = smoothedGrayFrame.Canny(new Gray(cannyThreshold), new Gray(cannyThreshold));
            //smoothing 模糊化
            if (blur)
                grayFrame = smoothedGrayFrame;
            //binarize 二值化
            CvInvoke.cvAdaptiveThreshold(grayFrame, grayFrame, 255, Emgu.CV.CvEnum.ADAPTIVE_THRESHOLD_TYPE.CV_ADAPTIVE_THRESH_MEAN_C, Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY, adaptiveThresholdBlockSize + adaptiveThresholdBlockSize % 2 + 1, adaptiveThresholdParameter);
            //
            grayFrame._Not();
            //
            if (addCanny)
                if (cannyFrame != null)
                    grayFrame._Or(cannyFrame);
            //
            this.binarizedFrame = grayFrame;

            //dilate canny contours for filtering 扩张多级边缘来过滤
            if (cannyFrame != null)
                cannyFrame = cannyFrame.Dilate(3);

            //find contours 找到轮廓
            var sourceContours = grayFrame.FindContours(Emgu.CV.CvEnum.CHAIN_APPROX_METHOD.CV_CHAIN_APPROX_NONE, Emgu.CV.CvEnum.RETR_TYPE.CV_RETR_LIST);
            //filter contours 过滤轮廓
            contours = FilterContours(sourceContours, cannyFrame, grayFrame.Width, grayFrame.Height);
            //find templates 找到模板
            lock (foundTemplates)
                foundTemplates.Clear();
            samples.Clear();

            lock (templates)
                Parallel.ForEach<Contour<Point>>(contours, (contour) =>
                {
                    var arr = contour.ToArray();
                    Template sample = new Template(arr, contour.Area, samples.templateSize);
                    lock (samples)
                        samples.Add(sample);

                    if (!onlyFindContours)
                    {
                        FoundTemplateDesc desc = finder.FindTemplate(templates, sample);

                        if (desc != null)
                            lock (foundTemplates)
                                foundTemplates.Add(desc);
                    }
                }
                );
            //
            FilterByIntersection(ref foundTemplates);
        }

        private static void FilterByIntersection(ref List<FoundTemplateDesc> templates)
        {
            //sort by area 按照面积分类
            templates.Sort(new Comparison<FoundTemplateDesc>((t1, t2) => -t1.sample.contour.SourceBoundingRect.Area().CompareTo(t2.sample.contour.SourceBoundingRect.Area())));
            //exclude templates inside other templates 排除其他模板
            HashSet<int> toDel = new HashSet<int>();
            for (int i = 0; i < templates.Count; i++)
            {
                if (toDel.Contains(i))
                    continue;
                Rectangle bigRect = templates[i].sample.contour.SourceBoundingRect;
                int bigArea = templates[i].sample.contour.SourceBoundingRect.Area();
                bigRect.Inflate(4, 4);
                for (int j = i + 1; j < templates.Count; j++)
                {
                    if (bigRect.Contains(templates[j].sample.contour.SourceBoundingRect))
                    {
                        double a = templates[j].sample.contour.SourceBoundingRect.Area();
                        if (a / bigArea > 0.9d)
                        {
                            //choose template by rate 按照比率选择模板
                            if (templates[i].rate > templates[j].rate)
                                toDel.Add(j);
                            else
                                toDel.Add(i);
                        }
                        else//delete tempate 删除模板
                            toDel.Add(j);
                    }
                }
            }
            List<FoundTemplateDesc> newTemplates = new List<FoundTemplateDesc>();
            for (int i = 0; i < templates.Count; i++)
                if (!toDel.Contains(i))
                    newTemplates.Add(templates[i]);
            templates = newTemplates;
        }

        private List<Contour<Point>> FilterContours(Contour<Point> contours, Image<Gray, byte> cannyFrame, int frameWidth, int frameHeight)
        {
            int maxArea = frameWidth * frameHeight / 5;
            var c = contours;
            List<Contour<Point>> result = new List<Contour<Point>>();
            while (c != null)
            {
                if (filterContoursBySize)
                    if (c.Total < minContourLength ||
                        c.Area < minContourArea || c.Area > maxArea ||
                        c.Area / c.Total <= minFormFactor)
                        goto next;

                if (noiseFilter)
                {
                    Point p1 = c[0];
                    Point p2 = c[(c.Total / 2) % c.Total];
                    if (cannyFrame[p1].Intensity <= double.Epsilon && cannyFrame[p2].Intensity <= double.Epsilon)
                        goto next;
                }
                result.Add(c);

            next:
                c = c.HNext;
            }

            return result;
        }
    }
}
