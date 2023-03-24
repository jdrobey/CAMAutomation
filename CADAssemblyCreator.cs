using System;
using System.IO;

using NXOpen;

namespace CAMAutomation
{
    public class CADAssemblyCreator
    {
        public CADAssemblyCreator()
        {
            m_manager = CAMAutomationManager.GetInstance();
        }


        ~CADAssemblyCreator()
        {
            // empty
        }


        public Part CreateAssembly()
        {
           try
            {
                // Create the Assembly part
                CreateAssemblyPart();

                // Initialize Fixture Handler()
                InitializeFixtureHandler();

                // Save the Assembly
                SaveAssembly();

                return m_assemblyPart;
            }
            catch (Exception ex)
            {
                m_manager.LogFile.AddError(ex.Message); 

                return null;
            }
        }


        private void CreateAssemblyPart()
        {
            // Part will be created in INCH following MISUMI recommendation
            string assemblyPath = Path.Combine(m_manager.OutputDirectory, "_MainAssembly.prt");
            m_assemblyPart = Utilities.NXOpenUtils.CreateNewCADPart(assemblyPath, true, BasePart.Units.Inches);

            // Add Fixture Type as attribute on the assembly part, so it can be retrieved by the MovieGenerator
            m_assemblyPart.SetUserAttribute("FIXTURE_TYPE", -1, m_manager.FixtureType, Update.Option.Now);
        }


        private void InitializeFixtureHandler()
        {
            // Initialize Fixture Handler and Generate the first fixture
            Utilities.CAMSingleFixtureHandler.FixtureType fixtureType = Utilities.CAMSingleFixtureHandler.GetFixtureTypeFromString(m_manager.FixtureType);
            m_manager.CAMFixtureHandler = new Utilities.CAMFixtureHandler(fixtureType, m_assemblyPart, m_manager.FixturePath, m_manager.OutputDirectory);
            m_manager.CAMFixtureHandler.GenerateNewFixture();

            // Initialize Clamping Configurator
            m_manager.CAMClampingConfigurator = new CAMClampingConfigurator(m_manager.CAMFixtureHandler,  m_manager.ObjectiveFunctions);
        }


        private void SaveAssembly()
        {
            // Save the Assembly
            m_assemblyPart.Save(BasePart.SaveComponents.True, BasePart.CloseAfterSave.False);
        }


        private Part m_assemblyPart;

        private CAMAutomationManager m_manager;
    }
}
