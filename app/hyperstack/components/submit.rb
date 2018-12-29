class Submit < HyperComponent

  render(DIV) do

    H1 { "Submit a crash report" }

    P {
      "If Thrive has crashed it will generate a '.dmp' file." +
        "The file will be named a bunch of hexadecimal characters and it will be in the " +
        "'Thrive/bin' (the same folder as the main executable) folder. An example name is " +
        "'e709d739-416a-43ee-7b902580-9f81d05a.dmp'"
    }

    P {
      "You can upload a crash dump (see above) with the form below."
    }

    P {
      SPAN {
        "Currently this website does not store reports. So please copy the report " +
        "and put it on "
      }
      A(href: "https://pastebin.com") {"pastebin"}
      SPAN {
        " (or another text pasting service) and share the link along with a report on "
      }

      A(href: "https://community.revolutionarygamesstudio.com/c/bug-reports") {
        "our Community Forums"
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
