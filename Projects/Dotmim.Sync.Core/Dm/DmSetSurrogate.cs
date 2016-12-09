﻿using Dotmim.Sync.Data;

using System;

using System.Globalization;

namespace Dotmim.Sync.Data.Surrogate
{
    /// <summary>
    /// Represents a surrogate of a DmSet object, which DotMim Sync uses during custom binary serialization.
    /// </summary>
    [Serializable]
    public class DmSetSurrogate : IDisposable
    {

        /// <summary>Gets or sets the name of the DmSet that the DmSetSurrogate object represents.</summary>
        public string DmsetName { get; set; }

        /// <summary>Gets or sets the locale information used to compare strings within the table.</summary>
        public string CultureInfoName { get; set; }

        /// <summary>Gets or sets the Case sensitive rul of the DmSet that the DmSetSurrogate object represents.</summary>
        public bool CaseSensitive { get; set; }

        /// <summary>Gets an array of DmTableSurrogate objects that comprise the dm set that is represented by the DmSetSurrogate object.</summary>
        public DmTableSurrogate[] DmTableSurrogates { get; set; }

        /// <summary>Initializes a new instance of the DmSetSurrogate class from an existing DmSet</summary>
        public DmSetSurrogate(DmSet ds)
        {
            if (ds == null)
                throw new ArgumentNullException("ds");


            this.DmsetName = ds.DmSetName;
            this.CultureInfoName = ds.Culture.Name;
            this.CaseSensitive = ds.CaseSensitive;

            this.DmTableSurrogates = new DmTableSurrogate[ds.Tables.Count];

            for (int i = 0; i < ds.Tables.Count; i++)
                this.DmTableSurrogates[i] = new DmTableSurrogate(ds.Tables[i]);
        }

        /// <summary>
        /// Only used for Serialization
        /// </summary>
        public DmSetSurrogate()
        {

        }

        /// <summary>
        /// Constructs a DmSet object based on a DmSetSurrogate object.
        /// [TODO] : Be Careful : the Primary Keys are not replicated !
        /// </summary>
        public DmSet ConvertToDmSet()
        {
            DmSet dmSet = new DmSet()
            {
                Culture = new CultureInfo(this.CultureInfoName),
                CaseSensitive = this.CaseSensitive
            };
            this.ReadSchemaIntoDmSet(dmSet);
            this.ReadDataIntoDmSet(dmSet);
            return dmSet;
        }

        /// <summary>
        /// Clone the originla DmSet and copy datas from the DmSetSurrogate
        /// </summary>
        public DmSet ConvertToDmSet(DmSet originalDmSet)
        {
            DmSet dmSet = originalDmSet.Clone();
            this.ReadDataIntoDmSet(dmSet);
            return dmSet;
        }

        internal void ReadDataIntoDmSet(DmSet ds)
        {
            for (int i = 0; i < ds.Tables.Count; i++)
            {
                DmTableSurrogate dmTableSurrogate = this.DmTableSurrogates[i];
                dmTableSurrogate.ReadDatasIntoDmTable(ds.Tables[i]);
            }
        }

        internal void ReadSchemaIntoDmSet(DmSet ds)
        {
            ds.DmSetName = this.DmsetName;
            ds.Culture = new CultureInfo(this.CultureInfoName);
            DmTableSurrogate[] dmTableSurrogateArray = this.DmTableSurrogates;
            for (int i = 0; i < (int)dmTableSurrogateArray.Length; i++)
            {
                DmTableSurrogate dmTableSurrogate = dmTableSurrogateArray[i];
                DmTable dmTable = new DmTable();
                dmTableSurrogate.ReadSchemaIntoDmTable(dmTable);

                dmTable.Culture = new CultureInfo(dmTableSurrogate.CultureInfoName);
                dmTable.CaseSensitive = dmTableSurrogate.CaseSensitive;
                dmTable.TableName = dmTableSurrogate.TableName;

                ds.Tables.Add(dmTable);
            }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool cleanup)
        {
            DmTableSurrogate[] dmTableSurrogateArray = this.DmTableSurrogates;
            for (int i = 0; i < (int)dmTableSurrogateArray.Length; i++)
                dmTableSurrogateArray[i].Dispose();

            this.DmTableSurrogates = null;
        }
    }
}