using UnityEngine;

namespace FaceQuest
{
    /// <summary>
    /// I comandi del visore, in un posto solo.
    ///
    /// La mappa e sui tasti e non sui bottoni in aria: puntare un bottone
    /// sospeso per accendere il riconoscimento e piu lento e meno preciso che
    /// premere un tasto, e in AR il pannello e spesso fuori dal campo visivo.
    /// Il puntatore resta, ma serve solo dove non c'e alternativa: scegliere una
    /// persona nella lista e scrivere il suo nome.
    ///
    ///   A            accende e spegne il riconoscimento
    ///   Y            cancella il database (con conferma)
    ///   B tenuto     esce dall'app
    ///   stick su/giu scorre la lista delle persone
    ///   stick premuto riporta il pannello davanti agli occhi
    ///   grilletto    preme cio che il raggio sta indicando,
    ///                e TENUTO trascina il pannello per la sua barra
    ///
    /// Ogni comando accetta anche l'equivalente sull'altro controller e, dove ha
    /// senso, il pizzico a mani nude: sul visore in uso risulta attivo il solo
    /// controller destro (ctrl:RTouch), e un comando legato a un tasto che sta
    /// solo sul sinistro sembra rotto quando in realta non e mai arrivato.
    /// </summary>
    public static class FaceComandi
    {
        /// <summary>Preme l'elemento puntato dal raggio: grilletto o pizzico indice.</summary>
        public static bool ClickPremuto() =>
            OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger) ||
            OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger) ||
            PinchIniziato(OVRPlugin.HandFingerPinch.Index);

        /// <summary>
        /// Grilletto TENUTO: e la presa con cui si trascina il pannello.
        ///
        /// Separato da ClickPremuto, che e il fronte di salita: premere e
        /// trascinare sono due gesti diversi sullo stesso dito, e il pizzico
        /// vale come il grilletto perche a mani nude non c'e altro.
        /// </summary>
        public static bool ClickTenuto() =>
            OVRInput.Get(OVRInput.RawButton.RIndexTrigger) ||
            OVRInput.Get(OVRInput.RawButton.LIndexTrigger) ||
            PinchAttivo(OVRPlugin.HandFingerPinch.Index);

        /// <summary>
        /// Riporta il pannello davanti agli occhi: pressione dello stick.
        ///
        /// Un pannello fermo nella stanza si puo perdere — basta voltarsi e
        /// camminare — e senza una via di ritorno l'unico rimedio sarebbe
        /// riavviare l'app. Lo stick premuto e un gesto che non capita per
        /// sbaglio mentre si scorre la lista.
        /// </summary>
        public static bool RicentraPremuto() =>
            OVRInput.GetDown(OVRInput.RawButton.RThumbstick) ||
            OVRInput.GetDown(OVRInput.RawButton.LThumbstick);

        /// <summary>Accende e spegne il riconoscimento: tasto A (o X sul sinistro).</summary>
        public static bool RiconoscimentoPremuto() =>
            OVRInput.GetDown(OVRInput.RawButton.A) ||
            OVRInput.GetDown(OVRInput.RawButton.X);

        /// <summary>Cancella il database: tasto Y. Apre la conferma, non cancella subito.</summary>
        public static bool CancellaDbPremuto() =>
            OVRInput.GetDown(OVRInput.RawButton.Y);

        /// <summary>
        /// Uscita: tasto B TENUTO. Tenuto e non premuto: B e sotto il pollice
        /// destro e una pressione involontaria chiuderebbe l'app nel mezzo di una
        /// prova.
        /// </summary>
        public static bool UscitaTenuta() =>
            OVRInput.Get(OVRInput.RawButton.B);
            // Nessun gesto a mani nude per l'uscita: un falso positivo del
            // tracciamento delle mani chiuderebbe l'app da solo, e dall'esterno
            // sarebbe indistinguibile da un crash.

        /// <summary>Asse verticale degli stick: positivo verso l'alto.</summary>
        public static float StickVerticale()
        {
            var destro = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).y;
            var sinistro = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).y;
            return Mathf.Abs(destro) >= Mathf.Abs(sinistro) ? destro : sinistro;
        }

        public static string Diagnostica() => $"ctrl:{OVRInput.GetConnectedControllers()}";

        private static readonly OVRPlugin.HandState[] s_stato = new OVRPlugin.HandState[2];
        private static readonly bool[,] s_prec = new bool[2, 4];

        private static bool PinchAttivo(OVRPlugin.HandFingerPinch dito)
        {
            for (var i = 0; i < 2; i++)
            {
                if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, (OVRPlugin.Hand)i, ref s_stato[i])) continue;
                if ((s_stato[i].Pinches & dito) != 0) return true;
            }
            return false;
        }

        private static bool PinchIniziato(OVRPlugin.HandFingerPinch dito)
        {
            var i0 = dito == OVRPlugin.HandFingerPinch.Index ? 0 :
                     dito == OVRPlugin.HandFingerPinch.Middle ? 1 : 2;
            var iniziato = false;
            for (var i = 0; i < 2; i++)
            {
                if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, (OVRPlugin.Hand)i, ref s_stato[i])) continue;
                var ora = (s_stato[i].Pinches & dito) != 0;
                if (ora && !s_prec[i, i0]) iniziato = true;
                s_prec[i, i0] = ora;
            }
            return iniziato;
        }
    }
}
