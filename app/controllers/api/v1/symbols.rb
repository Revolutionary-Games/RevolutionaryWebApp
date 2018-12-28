require 'fileutils'

module API
  module V1
    class Symbols < Grape::API
      include API::V1::Defaults

      resource :symbols do
        desc "Return all symbols"
        params do
          requires :key, type: String, desc: "API key"
        end
        get "", root: :graduates do

          if !ApiHelper::check_token permitted_params[:key]
            error!({error_code: 401, error_message: "Unauthorized."}, 401)
            return
          end

          DebugSymbol.all
        end

        desc "Return a symbol info"
        params do
          requires :key, type: String, desc: "API key"
          requires :id, type: String, desc: "ID of the symbol"
        end
        get ":id", root: "symbol" do

          if !ApiHelper::check_token permitted_params[:key]
            error!({error_code: 401, error_message: "Unauthorized."}, 401)
            return
          end

          DebugSymbol.where(id: permitted_params[:id]).first!
        end

        desc "Return a symbol by hash and name"
        params do
          requires :key, type: String, desc: "API key"
          requires :symbol_hash, type: String, desc: "Hash of the symbol"
          requires :name, type: String, desc: "Name of the symbol"
        end
        get "by_hash/:symbol_hash/:name", root: "symbol" do

          if !ApiHelper::check_token permitted_params[:key]
            error!({error_code: 401, error_message: "Unauthorized."}, 401)
            return
          end

          DebugSymbol.where(hash: permitted_params[:symbol_hash],
                            name: permitted_params[:name]).first!
        end

        desc "Return a symbol file contents"
        params do
          requires :key, type: String, desc: "API key"
          requires :id, type: String, desc: "ID of the symbol"
        end
        get "raw/:id", root: "symbol" do

          if !ApiHelper::check_token permitted_params[:key], access: :developer
            error!({error_code: 401, error_message: "Unauthorized."}, 401)
            return
          end

          symbol = DebugSymbol.where(id: permitted_params[:id]).first!

          content_type "application/octet-stream"
          header['Content-Disposition'] = "attachment; filename=#{symbol.name}.sym"
          env['api.format'] = :binary
          File.open(symbol.path).read
        end

        desc "Deletes a symbol by id"
        params do
          requires :key, type: String, desc: "API key"
          requires :id, type: String, desc: "ID of the symbol"
        end
        delete ":id", root: "symbol" do

          if !ApiHelper::check_token permitted_params[:key], access: :developer
            error!({error_code: 401, error_message: "Unauthorized."}, 401)
            return
          end

          symbol = DebugSymbol.find(permitted_params[:id])

          path = symbol.path

          symbol.destroy

          # Delete the file
          FileUtils.rm_f path

          return symbol
        end

        desc "Upload a symbol file."
        params do
          requires :key, type: String, desc: "API key"
          requires :data, type: File
        end
        post "" do

          if !ApiHelper::check_token permitted_params[:key], access: :developer
            error!({error_code: 401, error_message: "Unauthorized."}, 401)
            return
          end

          platform, arch, hash, name = ApiHelper::getBreakpadSymbolInfo(
                                  permitted_params[:data][:tempfile].first)


          if !hash || !name || hash.length < 1 || name.length < 1
            error!({error_code: 400, error_message: "Malformed symbol file"}, 400)
            return
          end

          FileUtils.mkdir_p File.join("SymbolData", name, hash)

          finalPath = File.join("SymbolData", name, hash, name + ".sym")

          FileUtils.cp permitted_params[:data][:tempfile].path, finalPath

          DebugSymbol.create(name: name, symbol_hash: hash, path: finalPath)
        end
      end
    end
  end
end
