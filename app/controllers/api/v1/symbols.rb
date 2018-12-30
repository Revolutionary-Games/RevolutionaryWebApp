require 'fileutils'

module API
  module V1
    class Symbols < Grape::API
      include API::V1::Defaults

      resource :symbols do
        desc "Return all symbols"
        params do
          requires :token, type: String, desc: "API token"
        end
        get "", root: :symbols do

          if !ApiHelper::check_token permitted_params[:token]
            error!({error_code: 401, error_message: "Unauthorized."}, 401)
            return
          end

          DebugSymbol.all
        end

        desc "Return a symbol by hash and name"
        params do
          requires :token, type: String, desc: "API token"
          requires :symbol_hash, type: String, desc: "Hash of the symbol"
          requires :name, type: String, desc: "Name of the symbol"
        end
        get "by_hash/:symbol_hash/:name", root: "symbol" do

          if !ApiHelper::check_token permitted_params[:token]
            error!({error_code: 401, error_message: "Unauthorized."}, 401)
            return
          end

          DebugSymbol.where(symbol_hash: permitted_params[:symbol_hash],
                            name: permitted_params[:name]).first!
        end

        desc "Return ids for all existing symbols by hash and name"
        params do
          requires :token, type: String, desc: "API token"
          requires :to_check, type: JSON, desc: "Hashes and names of the symbols to check for"
        end
        post "all", root: "symbol" do

          if !ApiHelper::check_token permitted_params[:token]
            error!({error_code: 401, error_message: "Unauthorized."}, 401)
            return
          end

          if !permitted_params[:to_check] || !permitted_params[:to_check].respond_to?('each')
            error!({error_code: 400, error_message: "No data to check"}, 400)
            return
          end

          errors = []
          existing = []

          permitted_params[:to_check].each{|entry|

            if !entry.include?(:hash) || !entry.include?(:name)
              errors.push({
                description: "entry with missing hash or name",
                entry: entry
              })
              next
            end

            if DebugSymbol.exists?(symbol_hash: entry[:hash], name: entry[:name])
              existing.push entry
            end
          }

          return {
            errors: errors,
            existing: existing
          }
        end

        desc "Return a symbol info"
        params do
          requires :token, type: String, desc: "API token"
          requires :id, type: String, desc: "ID of the symbol"
        end
        get ":id", root: "symbol" do

          if !ApiHelper::check_token permitted_params[:token]
            error!({error_code: 401, error_message: "Unauthorized."}, 401)
            return
          end

          DebugSymbol.where(id: permitted_params[:id]).first!
        end

        desc "Return a symbol file contents"
        params do
          requires :token, type: String, desc: "API token"
          requires :id, type: String, desc: "ID of the symbol"
        end
        get "raw/:id", root: "symbol" do

          if !ApiHelper::check_token permitted_params[:token], access: :developer
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
          requires :token, type: String, desc: "API token"
          requires :id, type: String, desc: "ID of the symbol"
        end
        delete ":id", root: "symbol" do

          if !ApiHelper::check_token permitted_params[:token], access: :developer
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
          requires :token, type: String, desc: "API token"
          requires :data, type: File
        end
        post "" do

          if !ApiHelper::check_token permitted_params[:token], access: :developer
            error!({error_code: 401, error_message: "Unauthorized."}, 401)
            return
          end

          if !permitted_params[:data]
            error!({error_code: 400, error_message: "Empty file"}, 400)
            return
          end

          begin
            platform, arch, hash, name = ApiHelper::getBreakpadSymbolInfo(
                                    permitted_params[:data][:tempfile].first)
          rescue
            error!({error_code: 400, error_message: "Malformed symbol file"}, 400)
            return
          end

          if name
            # Fix random junk at the end for windows
            name.strip!
          end

          if !hash || !name || hash.length < 1 || name.length < 1
            error!({error_code: 400, error_message: "Malformed symbol file"}, 400)
            return
          end

          # Create first to error fast
          finalPath = File.join("SymbolData", name, hash, name + ".sym")
          created = DebugSymbol.create(name: name, symbol_hash: hash, path: finalPath,
                                       size: File.size(permitted_params[:data][:tempfile].path))

          FileUtils.mkdir_p File.join("SymbolData", name, hash)

          FileUtils.cp permitted_params[:data][:tempfile].path, finalPath

          created
        end
      end
    end
  end
end
