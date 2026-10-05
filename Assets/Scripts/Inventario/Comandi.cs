using UnityEngine;

namespace InventarioAR
{
    /// <summary>
    /// Comandi che funzionano sia con i controller sia a mani nude.
    ///
    /// Serve perche quando il Quest e in modalita hand tracking i controller non
    /// sono attivi e OVRInput non riporta nulla: un comando legato al solo tasto
    /// sembra rotto quando in realta non e mai arrivato. Ogni azione ha quindi
    /// due strade, e chi usa l'app non deve sapere quale.
    ///
    /// A mani nude si guarda il pinch MANTENUTO, non l'istante in cui inizia:
    /// serve per l'uscita, che richiede una pressione prolungata.
    /// </summary>
    public static class Comandi
    {
        // Ogni comando accetta un tasto del controller DESTRO e uno del SINISTRO,
        // piu un gesto a mani nude. Il motivo e concreto: la diagnostica ha mostrato
        // "ctrl:RTouch", cioe solo il destro attivo, e i comandi assegnati a X e Y
        // (che stanno sul sinistro) non potevano funzionare. Accettarli tutti evita
        // di dover indovinare quale periferica sia sveglia.

        /// <summary>Scorri la selezione: A o X, oppure pizzico indice-pollice.</summary>
        public static bool ScorriPremuto() =>
            OVRInput.GetDown(OVRInput.RawButton.A) ||
            OVRInput.GetDown(OVRInput.RawButton.X) ||
            PinchIniziato(OVRPlugin.HandFingerPinch.Index);

        /// <summary>Annulla la selezione: click sullo stick destro o Y, oppure pizzico medio.</summary>
        public static bool AnnullaPremuto() =>
            OVRInput.GetDown(OVRInput.RawButton.RThumbstick) ||
            OVRInput.GetDown(OVRInput.RawButton.Y) ||
            PinchIniziato(OVRPlugin.HandFingerPinch.Middle);

        /// <summary>Azzeramento: click dello stick destro TENUTO premuto.</summary>
        public static bool AzzeramentoTenuto() =>
            OVRInput.Get(OVRInput.RawButton.RThumbstick) || OVRInput.Get(OVRInput.RawButton.LThumbstick);

        /// <summary>Uscita: tasto B tenuto premuto, oppure pizzico anulare mantenuto.</summary>
        public static bool UscitaTenuta() =>
            OVRInput.Get(OVRInput.RawButton.B) || PinchAttivo(OVRPlugin.HandFingerPinch.Ring);

        /// <summary>
        /// Stato grezzo degli ingressi, da mostrare nel pannello.
        ///
        /// Serve a distinguere "il comando non arriva" da "il comando arriva ma il
        /// codice non reagisce": due guasti diversi che dall'esterno si somigliano.
        /// </summary>
        public static string Diagnostica()
        {
            var ctrl = OVRInput.GetConnectedControllers();
            return $"ctrl:{ctrl} " +
                   $"A{(OVRInput.Get(OVRInput.RawButton.A) ? "+" : "-")}" +
                   $"B{(OVRInput.Get(OVRInput.RawButton.B) ? "+" : "-")}" +
                   $"X{(OVRInput.Get(OVRInput.RawButton.X) ? "+" : "-")}" +
                   $"Y{(OVRInput.Get(OVRInput.RawButton.Y) ? "+" : "-")} " +
                   $"pinch I{(PinchAttivo(OVRPlugin.HandFingerPinch.Index) ? "+" : "-")}" +
                   $"M{(PinchAttivo(OVRPlugin.HandFingerPinch.Middle) ? "+" : "-")}" +
                   $"R{(PinchAttivo(OVRPlugin.HandFingerPinch.Ring) ? "+" : "-")}";
        }

        // ----------------------------------------------------------------

        private static readonly OVRPlugin.HandState[] _stato = new OVRPlugin.HandState[2];
        private static readonly bool[,] _precedente = new bool[2, 4];

        private static bool PinchAttivo(OVRPlugin.HandFingerPinch dito)
        {
            for (var i = 0; i < 2; i++)
            {
                if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, (OVRPlugin.Hand)i, ref _stato[i])) continue;
                if ((_stato[i].Pinches & dito) != 0) return true;
            }
            return false;
        }

        private static bool PinchIniziato(OVRPlugin.HandFingerPinch dito)
        {
            var indiceDito = IndiceDi(dito);
            var iniziato = false;

            for (var i = 0; i < 2; i++)
            {
                if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, (OVRPlugin.Hand)i, ref _stato[i])) continue;

                var ora = (_stato[i].Pinches & dito) != 0;
                if (ora && !_precedente[i, indiceDito]) iniziato = true;
                _precedente[i, indiceDito] = ora;
            }

            return iniziato;
        }

        private static int IndiceDi(OVRPlugin.HandFingerPinch dito) => dito switch
        {
            OVRPlugin.HandFingerPinch.Index => 0,
            OVRPlugin.HandFingerPinch.Middle => 1,
            OVRPlugin.HandFingerPinch.Ring => 2,
            _ => 3,
        };
    }
}
