class CrashDumpTool < HyperComponent

  render(DIV) do

    H1 { "Decode a crashdump created by Thrive" }

    H2 { "The Thrive Launcher provides a tool for creating a full crash report. Use that " +
         "instead. Unless you specifically want to see what is inside a crashdump file"
    }

    P {
      "If Thrive has crashed it will generate a '.dmp' file." +
        "The file will be named a bunch of hexadecimal characters and it will be in the " +
        "'Thrive/bin' (the same folder as the main executable) folder. An example name is " +
        "'e709d739-416a-43ee-7b902580-9f81d05a.dmp'"
    }

    P {
      "You can upload a crash dump (see above) with the form below. " +
        "To get it in processed form."
    }

    P {
      SPAN {
        "This tool does not store the dumps. So please copy the report " +
        "and put it on "
      }
      A(href: "https://pastebin.com") {"pastebin"}
      SPAN {
        " (or another text pasting service)"
      }
    }

    FORM(method: "post", action: "/api/v1/stackwalk", enctype: "multipart/form-data") do
      LABEL{ "Please select the crash dump file: " }
      INPUT(type: :file, name: "data")
      BR{}
      BUTTON(type: "submit") { "Submit" }
    end    
    
    
  end
end
